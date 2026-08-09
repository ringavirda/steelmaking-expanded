using ExpandedLib.Blocks.Networks;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// Invisible, solid placeholder that fills the grid cells a mega-block visually
/// occupies (see <see cref="StructureFillers"/>). It renders nothing but provides
/// real per-cell collision/selection, and reroutes every player-facing operation
/// to the "principal" controller block recorded on its
/// <see cref="BlockEntityStructureFiller"/> - mirroring vanilla's
/// <c>BlockMPMultiblockGear</c>.
/// </summary>
[BlockRegister]
public partial class BlockStructureFiller
  : Block,
    INetworkConnector,
    IMechanicalPowerBlock {
  // INetworkConnector: a plain filler is inert (type ""), but a principal can turn one cell into
  // a fixed port by setting PortFace/PortNetworkType on its BE (e.g. the boiler's steam outlet).
  // The position-less members are the inert fallback; the network system uses the position-aware
  // overloads, which consult the BE.
  public string NetworkType => "";

  public bool HasConnectorAt(BlockFacing face) => false;

  public string NetworkTypeAt(IBlockAccessor world, BlockPos pos) =>
    world.GetBlockEntity(pos) is BlockEntityStructureFiller be
      ? be.PortNetworkType ?? ""
      : "";

  public bool HasConnectorAt(
    IBlockAccessor world,
    BlockPos pos,
    BlockFacing face
  ) =>
    world.GetBlockEntity(pos) is BlockEntityStructureFiller be
    && be.PortFace == face.Code[0].ToString();

  // IMechanicalPowerBlock: a footprint cell becomes a mechanical-power intake when its
  // fillerOffsets entry hosts an MP behaviour (e.g. exlib.BEBehaviorMPFillerPort) with a connector
  // face. The vanilla MP network discovers power by asking the block at each cell whether it
  // accepts an axle on a given face.
  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
#if GAME_GE_1_22
    ,
    BlockMPBase forBlock
#endif
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(pos)
        is not BlockEntityStructureFiller be
      || be.GetBehavior<BEBehaviorMPBase>() == null
      || be.HostedBehaviors == null
    )
      return false;
    // A mechanical axle couples along an axis, so a port declared on one face accepts a connection
    // on that face or its opposite (both ends of the axle line), letting an axle run straight
    // through the cell and attach from either side.
    foreach (FillerBehavior b in be.HostedBehaviors)
      if (
        b.ConnectorFace != null
        && (b.ConnectorFace == face || b.ConnectorFace.Opposite == face)
      )
        return true;
    return false;
  }

  public void DidConnectAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
  ) { }

  /// <summary>The MP network of the hosted port behaviour at this cell, or null when it hosts none.</summary>
  public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
    world
      .BlockAccessor.GetBlockEntity(pos)
      ?.GetBehavior<BEBehaviorMPBase>()
      ?.Network;

  // Fillers are solid so they collide, but by default aren't an attachment surface (else
  // torches/vines/slabs could hang on the invisible footprint). A cell opts back in via its
  // fillerOffsets "allowAttach" flag.
  public override bool CanAttachBlockAt(
    IBlockAccessor blockAccessor,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi? attachmentArea = null
  ) {
    if (
      blockAccessor.GetBlockEntity(pos) is BlockEntityStructureFiller be
      && be.AllowAttach
    )
      return base.CanAttachBlockAt(
        blockAccessor,
        block,
        pos,
        blockFace,
        attachmentArea
      );
    return false;
  }

  /// <summary>
  /// Inherits the principal's interaction sounds so the invisible footprint sounds like the block
  /// it stands in for (otherwise hitting/walking on a filler is silent).
  /// </summary>
  public override BlockSounds GetSounds(
    IBlockAccessor blockAccessor,
    BlockSelection blockSel,
    ItemStack? stack = null
  ) {
    if (
      blockSel?.Position != null
      && blockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityStructureFiller be
      && be.Principal != null
    ) {
      Block principal = blockAccessor.GetBlock(be.Principal);
      if (principal.Id != 0 && principal != this)
        return principal.GetSounds(blockAccessor, blockSel, stack);
    }
    return base.GetSounds(blockAccessor, blockSel, stack);
  }

  /// <summary>Resolves the principal position + block, or null when orphaned.</summary>
  private bool TryGetPrincipal(
    IWorldAccessor world,
    BlockPos pos,
    out BlockPos principalPos,
    out Block principalBlock
  ) {
    principalPos = null!;
    principalBlock = null!;
    if (
      world.BlockAccessor.GetBlockEntity(pos)
        is not BlockEntityStructureFiller be
      || be.Principal == null
    )
      return false;

    principalPos = be.Principal;
    principalBlock = world.BlockAccessor.GetBlock(principalPos);
    return principalBlock.Id != 0;
  }

  private static BlockSelection Repoint(
    BlockSelection sel,
    BlockPos principalPos
  ) {
    BlockSelection clone = sel.Clone();
    clone.Position = principalPos;
    return clone;
  }

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    // A held placeable block means the player is building on the footprint, not driving the
    // principal - skip the forward. Liquid containers are the exception: the principal (e.g. a
    // boiler pouring from a bucket) still needs to see them, so those forward.
    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    bool placingBlock =
      held?.Block != null && held.Collectible is not BlockLiquidContainerBase;

    // Forward to the principal first; if it handles the click we're done. Cell-aware principals
    // (IFillerInteractionTarget) also get the clicked cell, to restrict an interaction to it.
    if (
      !placingBlock
      && TryGetPrincipal(world, blockSel.Position, out var pp, out var pb)
    ) {
      BlockSelection psel = Repoint(blockSel, pp);
      bool handled = pb is IFillerInteractionTarget target
        ? target.OnFillerInteractStart(world, byPlayer, psel, blockSel.Position)
        : pb.OnBlockInteractStart(world, byPlayer, psel);
      if (handled)
        return true;
    }

    // Unhandled: on an allowAttach cell (a buildable surface), return false so the engine does
    // its normal placement on the filler's face instead of swallowing the click.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityStructureFiller be
      && be.AllowAttach
    )
      return false;

    // Non-buildable cell: swallow a block-placement click so no block drops on the filler's face.
    if (placingBlock)
      return true;

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb))
      return base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);
    BlockSelection psel = Repoint(blockSel, pp);
    return pb is IFillerInteractionTarget target
      ? target.OnFillerInteractStep(
        secondsUsed,
        world,
        byPlayer,
        psel,
        blockSel.Position
      )
      : pb.OnBlockInteractStep(secondsUsed, world, byPlayer, psel);
  }

  public override void OnBlockInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb)) {
      base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
      return;
    }
    BlockSelection psel = Repoint(blockSel, pp);
    if (pb is IFillerInteractionTarget target)
      target.OnFillerInteractStop(
        secondsUsed,
        world,
        byPlayer,
        psel,
        blockSel.Position
      );
    else
      pb.OnBlockInteractStop(secondsUsed, world, byPlayer, psel);
  }

  public override float OnGettingBroken(
    IPlayer player,
    BlockSelection blockSel,
    ItemSlot itemslot,
    float remainingResistance,
    float dt,
    int counter
  ) {
    IWorldAccessor world = player?.Entity?.World ?? api.World;
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb))
      return base.OnGettingBroken(
        player,
        blockSel,
        itemslot,
        remainingResistance,
        dt,
        counter
      );
    return pb.OnGettingBroken(
      player,
      Repoint(blockSel, pp),
      itemslot,
      remainingResistance,
      dt,
      counter
    );
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    // Breaking any filler breaks the whole structure: the principal's OnBlockBroken clears every
    // filler cell via StructureFillers.RemoveFillers. Orphaned fillers fall back to a plain remove.
    if (!TryGetPrincipal(world, pos, out var pp, out var pb)) {
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
      return;
    }
    pb.OnBlockBroken(world, pp, byPlayer, dropQuantityMultiplier);

    // Safety net: if the principal's break didn't clear us, don't linger as an orphan.
    if (world.BlockAccessor.GetBlock(pos).Id == BlockId)
      world.BlockAccessor.SetBlock(0, pos);
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) {
    if (!TryGetPrincipal(world, pos, out var pp, out var pb))
      return base.OnPickBlock(world, pos);
    return pb.OnPickBlock(world, pp);
  }

  // The principal owns all drops; a filler never drops anything itself.
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) => [];

  // The look-at info HUD shows the principal's text (e.g. the pump's status) instead of the
  // invisible filler's, so any footprint cell reads like the block it stands in for.
  public override string GetPlacedBlockInfo(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer forPlayer
  ) {
    if (!TryGetPrincipal(world, pos, out var pp, out var pb))
      return base.GetPlacedBlockInfo(world, pos, forPlayer);
    return pb.GetPlacedBlockInfo(world, pp, forPlayer);
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    if (!TryGetPrincipal(world, selection.Position, out var pp, out var pb))
      return base.GetPlacedBlockInteractionHelp(world, selection, forPlayer);
    BlockSelection psel = Repoint(selection, pp);
    return pb is IFillerInteractionTarget target
      ? target.GetFillerInteractionHelp(
        world,
        psel,
        forPlayer,
        selection.Position
      )
      : pb.GetPlacedBlockInteractionHelp(world, psel, forPlayer);
  }
}

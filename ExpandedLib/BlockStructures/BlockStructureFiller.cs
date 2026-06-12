using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace ExpandedLib.BlockStructures;

/// <summary>
/// Invisible, solid placeholder that fills the grid cells a mega-block visually
/// occupies (see <see cref="StructureFillers"/>). It renders nothing but provides
/// real per-cell collision/selection, and reroutes every player-facing operation
/// to the "principal" controller block recorded on its
/// <see cref="BlockEntityStructureFiller"/> — mirroring vanilla's
/// <c>BlockMPMultiblockGear</c>.
/// </summary>
[EntityRegister]
public class BlockStructureFiller : Block, INetworkConnector
{
  // --- INetworkConnector: optional per-cell network port ---
  // A plain filler is inert (network type ""), so it never affects pipe orientation or
  // connectivity. A principal can turn one footprint cell into a fixed port by setting
  // PortFace/PortNetworkType on that cell's BE (e.g. the boiler's steam outlet sits on the
  // filler directly below the steam pipe); only that cell then reads as a connector, on that
  // one face. The position-less members below are the inert fallback; the network system uses
  // the position-aware overloads, which consult the BE.
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

  // Fillers are solid (sidesolid:all) so they collide, but by default they must not
  // act as an attachment surface — otherwise torches, vines, slabs, etc. could be
  // hung on the invisible footprint. A cell opts back in via its fillerOffsets
  // "allowAttach" flag, recorded on the BE at placement time.
  public override bool CanAttachBlockAt(
    IBlockAccessor blockAccessor,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi? attachmentArea = null
  )
  {
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
  /// Inherits the principal's interaction sounds (hit, break, place, walk, inside,
  /// ambient) so the invisible footprint sounds like the block it stands in for —
  /// otherwise hitting or walking on a filler is silent. Every sound the engine plays
  /// for a block is resolved through this method.
  /// </summary>
  public override BlockSounds GetSounds(
    IBlockAccessor blockAccessor,
    BlockSelection blockSel,
    ItemStack? stack = null
  )
  {
    if (
      blockSel?.Position != null
      && blockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityStructureFiller be
      && be.Principal != null
    )
    {
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
  )
  {
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
  )
  {
    BlockSelection clone = sel.Clone();
    clone.Position = principalPos;
    return clone;
  }

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    // A held placeable block means the player wants to build on the footprint, not drive
    // the principal — skip the forward so the placement runs (the allowAttach branch below,
    // or base). Liquid containers are the one exception: a held bucket is itself a block,
    // but the principal (e.g. a boiler pouring from it) needs to see it, so those still
    // forward.
    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    bool placingBlock =
      held?.Block != null && held.Collectible is not BlockLiquidContainerBase;

    // Forward to the principal first. If it handles the click (e.g. a boiler pouring from a
    // held bucket or toggling its lid) we are done. Cell-aware principals
    // (IFillerInteractionTarget) also get the originally-clicked cell, so they can restrict
    // an interaction to a specific footprint cell.
    if (
      !placingBlock
      && TryGetPrincipal(world, blockSel.Position, out var pp, out var pb)
    )
    {
      BlockSelection psel = Repoint(blockSel, pp);
      bool handled = pb is IFillerInteractionTarget target
        ? target.OnFillerInteractStart(world, byPlayer, psel, blockSel.Position)
        : pb.OnBlockInteractStart(world, byPlayer, psel);
      if (handled)
        return true;
    }

    // The principal didn't handle it. On a cell flagged allowAttach (a buildable
    // surface, e.g. a boiler's deck), return false so the engine performs its normal
    // block placement on the filler's face instead of the click being swallowed —
    // otherwise only sneak+RMB would place a block here.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityStructureFiller be
      && be.AllowAttach
    )
      return false;

    // Cell is not a buildable surface: swallow a block-placement click so the engine
    // can't drop a block on the filler's face. Other interactions still fall through
    // to base.
    if (placingBlock)
      return true;

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
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
  )
  {
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb))
    {
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
  )
  {
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
  )
  {
    // Breaking any filler breaks the whole structure: forward to the principal,
    // whose OnBlockBroken clears every filler cell (including this one) via
    // StructureFillers.RemoveFillers. Orphaned fillers fall back to a plain remove.
    if (!TryGetPrincipal(world, pos, out var pp, out var pb))
    {
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
      return;
    }
    pb.OnBlockBroken(world, pp, byPlayer, dropQuantityMultiplier);

    // Safety net: if the principal's break did not clear us (e.g. mismatched
    // footprint), make sure this cell does not linger as an orphan filler.
    if (world.BlockAccessor.GetBlock(pos).Id == BlockId)
      world.BlockAccessor.SetBlock(0, pos);
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
  {
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

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
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

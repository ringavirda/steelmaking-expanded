using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

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
public class BlockStructureFiller : Block
{
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
    if (!TryGetPrincipal(world, blockSel.Position, out var pp, out var pb))
      return base.OnBlockInteractStart(world, byPlayer, blockSel);
    return pb.OnBlockInteractStart(world, byPlayer, Repoint(blockSel, pp));
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
    return pb.OnBlockInteractStep(
      secondsUsed,
      world,
      byPlayer,
      Repoint(blockSel, pp)
    );
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
    pb.OnBlockInteractStop(secondsUsed, world, byPlayer, Repoint(blockSel, pp));
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
    return pb.GetPlacedBlockInteractionHelp(
      world,
      Repoint(selection, pp),
      forPlayer
    );
  }
}

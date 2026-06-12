using System;
using ExpandedLib;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.Converter.Blocks;

/// <summary>
/// The 3×3×3 converter vessel block. Construction is driven by the
/// RightClickConstructable block-entity behavior; this block scatters any
/// solidified charge when broken with a steel-tier pickaxe.
/// </summary>
[EntityRegister]
public class BlockConverterBessemer : Block
{
  // RMB construction and its build prompts are routed to the
  // RightClickConstructable block-entity behaviour by the "BlockEntityInteract"
  // block behaviour declared in the block JSON.

  // The converter is welded steel — breaking it needs a steel-tier pickaxe.
  // The actual mining requirement is enforced via "requiredMiningTier" in the
  // block JSON; here we just scatter whatever solidified charge it held.
  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    ItemStack? solidifiedDrops = null;
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityConverterBessemer be
    )
      solidifiedDrops = be.CollectBreakDrops();

    // Clear the reserved 3x3x3 filler volume. Done before base.OnBlockBroken so
    // that even if the construction-drop path below throws, the fillers are gone
    // and we don't leave invisible solid cells behind.
    int fillerAngle = ExOrientation.AngleFromSide(Variant["side"]);
    var fillerCells = StructureFillers.FootprintCells(this, pos, fillerAngle);
    StructureFillers.RemoveFillers(world, pos, fillerCells);

    // base.OnBlockBroken drives the RightClickConstructable behaviour, which
    // resolves each completed stage's ingredients back into drops. That path
    // expands wildcard codes (e.g. metalplate-*) using the wildcard values
    // captured at build time; a converter raised before "storeWildCard" was
    // added to the recipe has none stored, so vanilla GetDrops throws while
    // expanding the "*" — and because it runs before the block is cleared, the
    // exception escapes to the client and crashes the game. Guard it so a
    // legacy/corrupt construction state degrades to "no construction drops"
    // and the block is still removed.
    try
    {
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    }
    catch (Exception e)
    {
      world.Logger.Warning(
        "[smex] Bessemer converter at {0} could not drop its construction "
          + "materials (likely built before the recipe fix); removing it "
          + "anyway. {1}",
        pos,
        e
      );
      if (world.BlockAccessor.GetBlock(pos) == this)
        world.BlockAccessor.SetBlock(0, pos);
    }

    if (solidifiedDrops != null && world.Side == EnumAppSide.Server)
      world.SpawnItemEntity(solidifiedDrops, pos.ToVec3d().Add(0.5, 0.5, 0.5));
  }
}

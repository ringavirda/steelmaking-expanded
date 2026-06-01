using SteelmakingExpanded.Structures.BessemerConverter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures.BessemerConverter.Blocks;

/// <summary>
/// The 3×3×3 converter vessel block. Construction is driven by the
/// RightClickConstructable block-entity behavior; this block scatters any
/// solidified charge when broken with a steel-tier pickaxe.
/// </summary>
public class BlockBessemerConverter : Block
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
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityBessemerConverter be
    )
      solidifiedDrops = be.CollectBreakDrops();

    // Lets the RightClickConstructable behaviour drop its built-up parts.
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);

    if (solidifiedDrops != null && world.Side == EnumAppSide.Server)
      world.SpawnItemEntity(solidifiedDrops, pos.ToVec3d().Add(0.5, 0.5, 0.5));
  }
}

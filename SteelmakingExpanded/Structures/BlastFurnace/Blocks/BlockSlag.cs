using SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures.BlastFurnace.Blocks;

/// <summary>Solidified slag block left when blast mix finishes burning; drops slag items scaled to its stored count.</summary>
public class BlockSlag : Block
{
  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    if (worldMap.BlockAccessor.GetBlockEntity(pos) is BlockEntitySlag be)
    {
      Item? slagItem = worldMap.GetItem(new AssetLocation("smex", "slag"));
      if (slagItem != null && be.SlagCount > 0)
      {
        // Randomize the drop slightly (e.g. 80-100% of the original mix)
        int dropCount = (int)(
          be.SlagCount * (0.8f + (worldMap.Rand.NextDouble() * 0.2f))
        );
        if (dropCount <= 0)
          dropCount = 1;
        return [new ItemStack(slagItem, dropCount)];
      }
    }
    return base.GetDrops(worldMap, pos, byPlayer, dropQuantityMultiplier);
  }
}

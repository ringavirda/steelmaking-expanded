using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>Solidified slag block left when burden finishes burning; drops slag items scaled to its stored count.</summary>
[BlockRegister]
public partial class BlockSlag : Block {
  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    if (worldMap.BlockAccessor.GetBlockEntity(pos) is BlockEntitySlag be) {
      Item? slagItem = worldMap.GetItem(new AssetLocation("smex", "slag"));
      if (slagItem != null && be.SlagCount > 0) {
        // Randomize the drop slightly (e.g. 80-100% of the stored count)
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

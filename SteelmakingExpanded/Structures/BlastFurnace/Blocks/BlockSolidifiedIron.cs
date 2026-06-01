using SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures.BlastFurnace.Blocks;

/// <summary>Solidified iron block left when a lit furnace is extinguished; drops iron bits scaled to its stored count.</summary>
public class BlockSolidifiedIron : Block
{
  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    if (
      worldMap.BlockAccessor.GetBlockEntity(pos) is BlockEntitySolidifiedIron be
    )
    {
      Item? bit = worldMap.GetItem(new AssetLocation("game", "metalbit-iron"));
      if (bit != null && be.IronCount > 0)
      {
        return [new ItemStack(bit, be.IronCount)];
      }
    }
    return base.GetDrops(worldMap, pos, byPlayer, dropQuantityMultiplier);
  }
}

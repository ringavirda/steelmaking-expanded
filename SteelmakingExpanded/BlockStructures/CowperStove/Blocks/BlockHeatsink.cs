using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures.Metalworking.CowperStove.Blocks;

/// <summary>
/// Heat sink block on the cowper stove: glows with the stored regenerator heat and
/// seals against passthrough pipes. Always drops/picks as the canonical north variant.
/// </summary>
[EntityRegister]
public class BlockHeatSink : Block, IPipeSealingBlock
{
  public override byte[] GetLightHsv(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    ItemStack? stack = null
  )
  {
    if (
      pos != null
      && blockAccessor.GetBlockEntity(pos) is BlockEntityHeatSink hs
    )
    {
      if (hs.Temperature > 500f)
      {
        byte val = (byte)GameMath.Clamp((hs.Temperature - 500f) / 30f, 0, 24);
        return [8, 7, val];
      }
    }
    return base.GetLightHsv(blockAccessor, pos, stack);
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
  {
    return new ItemStack(
      world.GetBlock(CodeWithVariant("side", "north")) ?? this
    );
  }

  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    return
    [
      new ItemStack(
        worldMap.GetBlock(CodeWithVariant("side", "north")) ?? this
      ),
    ];
  }
}

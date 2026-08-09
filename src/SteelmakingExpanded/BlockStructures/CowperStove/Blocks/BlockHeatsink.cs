using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.CowperStove.Blocks;

/// <summary>
/// Heat sink block on the cowper stove: glows with the stored regenerator heat.
/// Always drops/picks as the canonical north variant.
/// </summary>
[BlockRegister]
public partial class BlockHeatSink : Block {
  /// <summary>Appends the refractory tier, so the tiers are distinguishable in the inventory,
  /// handbook and look-at HUD rather than reading as one block.</summary>
  public override string GetHeldItemName(ItemStack itemStack) =>
    ExBlockNames.Decorate(this, base.GetHeldItemName(itemStack));

  public override byte[] GetLightHsv(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    ItemStack? stack = null
  ) {
    if (
      pos != null
      && blockAccessor.GetBlockEntity(pos) is BlockEntityHeatSink hs
    ) {
      byte val = MoltenMetal.GlowLevel(hs.Temperature);
      if (val > 0)
        return [8, 7, val];
    }
    return base.GetLightHsv(blockAccessor, pos, stack);
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) {
    return new ItemStack(
      world.GetBlock(CodeWithVariant("side", "north")) ?? this
    );
  }

  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    return
    [
      new ItemStack(
        worldMap.GetBlock(CodeWithVariant("side", "north")) ?? this
      ),
    ];
  }
}

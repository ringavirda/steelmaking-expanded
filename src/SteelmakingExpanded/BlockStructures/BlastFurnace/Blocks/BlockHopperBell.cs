using System.Collections.Generic;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>
/// The bell hopper block; its <see cref="BlockEntityHopperBell"/> crafts burden
/// and drops it into the furnace.
/// </summary>
[BlockRegister]
public partial class BlockHopperBell : Block {
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    var drops = new List<ItemStack>(
      base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier)
    );

    // Return the burden buffered in the internal magazine so it isn't lost.
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityHopperBell be
      && be.BurdenMagazine > 0
    ) {
      Item? burden = world.GetItem(new AssetLocation("smex", "burden"));
      if (burden != null)
        drops.Add(new ItemStack(burden, be.BurdenMagazine));
    }

    return drops.ToArray();
  }
}

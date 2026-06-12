using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>The bell hopper block; its <see cref="BlockEntityHopperBell"/> crafts blast mix and drops it into the furnace.</summary>
[EntityRegister]
public class BlockHopperBell : Block
{
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    var drops = new List<ItemStack>(
      base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier)
    );

    // Return the blast mix buffered in the internal magazine so it isn't lost.
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityHopperBell be
      && be.BlastMixMagazine > 0
    )
    {
      Item? blastmix = world.GetItem(new AssetLocation("smex", "blastmix"));
      if (blastmix != null)
        drops.Add(new ItemStack(blastmix, be.BlastMixMagazine));
    }

    return drops.ToArray();
  }
}

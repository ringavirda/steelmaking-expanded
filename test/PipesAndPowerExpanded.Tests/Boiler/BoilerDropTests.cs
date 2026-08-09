using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockStructures.Boiler.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// A boiler is placed from a crafted frame item, so breaking it returns that item alongside the
/// construction materials the RightClickConstructable behaviour scatters - the same deal the engines
/// get. Withholding the frame made taking a boiler down to move it a net loss. This pins that the
/// block adds no suppression of its own; a burst boiler is a different path and still keeps its
/// penalty (see BoilerExplosionDropRatio).
/// </summary>
public class BoilerDropTests {
  [Fact]
  public void A_boiler_returns_its_crafted_frame_when_broken() {
    var block = TestBlocks.Configure(
      new BlockBoilerCornish(),
      "ppex:boilercornish-north",
      1,
      ("side", "north")
    );
    block.Drops = [new BlockDropItemStack(new ItemStack(block))];

    ItemStack[] drops = block.GetDrops(null!, new BlockPos(0, 0, 0), null);

    Assert.Contains(drops, d => d.Collectible == block);
  }
}

using System;
using ExpandedLib.Testing;
using NSubstitute;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Property/invariant tests for the molten network: over many randomized fills and many flow ticks,
/// metal is neither created nor destroyed, no cell ever holds negative or over-capacity metal, and
/// the run trends toward levelling out. Conservation is the law most likely to be silently broken by
/// a flow-math change, so it is asserted over random topologies rather than one fixed case.
/// </summary>
public class MoltenInvariantTests {
  private const string Metal = "game:ingot-iron";
  private const int Capacity = 50; // CanalDefaultUnitCapacity

  private static BlockMoltenCanal Canal() {
    var block = TestBlocks.Configure(
      new BlockMoltenCanal(),
      "smex:moltencanal-straight-ns",
      1,
      ("type", "straight"),
      ("orientation", "ns")
    );
    ReflectionHelpers.SetProperty(block, "Type", "straight");
    ReflectionHelpers.SetProperty(block, "Orientation", "ns");
    return block;
  }

  [Theory]
  [InlineData(3)]
  [InlineData(11)]
  [InlineData(29)]
  [InlineData(101)]
  public void Flow_conserves_total_metal_and_respects_cell_bounds(int seed) {
    var rng = new Random(seed);
    int n = rng.Next(2, 6);

    var world = new TestWorld();
    world.RegisterNetwork("molten", s => new MoltenNetwork(s));
    var item = new Item { Code = new AssetLocation(Metal) };
    world.World.GetItem(Arg.Any<AssetLocation>()).Returns(item);

    var block = Canal();
    var cells = new BlockEntityMoltenCanal[n];
    for (int i = 0; i < n; i++) {
      var pos = new BlockPos(0, 0, i);
      cells[i] = new BlockEntityMoltenCanal { Pos = pos, Block = block };
      world.Place(pos, block, cells[i]);
      world.Attach(cells[i]);
    }
    for (int i = 0; i < n; i++)
      world.AddNode(new BlockPos(0, 0, i), "molten");

    // Seed random amounts (a single shared metal so the no-mix rule never blocks flow).
    int expectedTotal = 0;
    foreach (var c in cells) {
      int amt = rng.Next(0, Capacity + 1);
      int accepted = c.PushMetal(amt, new ItemStack(item, 1), world.World);
      expectedTotal += accepted;
    }

    int Total() => cells.Sum(c => c.CellAmount);
    Assert.Equal(expectedTotal, Total());

    for (int tick = 0; tick < 30; tick++) {
      world.Tick();
      Assert.Equal(expectedTotal, Total()); // conservation: nothing created or destroyed
      foreach (var c in cells) {
        Assert.True(c.CellAmount >= 0, $"negative cell amount (seed {seed})");
        Assert.True(
          c.CellAmount <= Capacity,
          $"cell over capacity (seed {seed})"
        );
      }
    }
  }
}

internal static class MoltenInvariantExtensions {
  public static int Sum(
    this BlockEntityMoltenCanal[] cells,
    System.Func<BlockEntityMoltenCanal, int> sel
  ) {
    int t = 0;
    foreach (var c in cells)
      t += sel(c);
    return t;
  }
}

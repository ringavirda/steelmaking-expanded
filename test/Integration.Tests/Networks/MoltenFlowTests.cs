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
/// Drives the molten network end to end: real canal cells own their metal, the network's per-tick
/// pass flows it across connectors and conserves the total. A fake "ingot" item stands in for the
/// resolved game collectible so the temperature carrier works headlessly.
/// </summary>
public class MoltenFlowTests {
  private const string Metal = "game:ingot-iron";

  /// <summary>A configured ns molten-canal block: connectors come straight off the orientation
  /// string, so reflection-setting Type/Orientation (OnLoaded is skipped) is enough.</summary>
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

  /// <summary>A two-cell molten run along +Z, both cells in one network, with the world wired to
  /// resolve the fake metal item.</summary>
  private static (
    TestWorld world,
    BlockEntityMoltenCanal a,
    BlockEntityMoltenCanal b
  ) Run() {
    var world = new TestWorld();
    world.RegisterNetwork("molten", sys => new MoltenNetwork(sys));

    var item = new Item { Code = new AssetLocation(Metal) };
    world.World.GetItem(Arg.Any<AssetLocation>()).Returns(item);

    var block = Canal();
    var a = new BlockEntityMoltenCanal {
      Pos = new BlockPos(0, 0, 0),
      Block = block,
    };
    var b = new BlockEntityMoltenCanal {
      Pos = new BlockPos(0, 0, 1),
      Block = block,
    };
    world.Place(a.Pos, block, a);
    world.Place(b.Pos, block, b);
    world.Attach(a);
    world.Attach(b);

    world.AddNode(a.Pos, "molten");
    world.AddNode(b.Pos, "molten");
    return (world, a, b);
  }

  private static ItemStack MetalStack(TestWorld world) =>
    new(world.World.GetItem(new AssetLocation(Metal)), 1);

  [Fact]
  public void Both_cells_join_one_network() {
    var (world, a, b) = Run();
    Assert.Same(world.NetworkAt(a.Pos), world.NetworkAt(b.Pos));
  }

  [Fact]
  public void PushMetal_fills_a_cell_up_to_its_capacity() {
    var (world, a, _) = Run();

    int accepted = a.PushMetal(40, MetalStack(world), world.World);

    Assert.Equal(40, accepted);
    Assert.Equal(40, a.CellAmount);
    Assert.Equal(Metal, a.CellMetalType);
    Assert.True(a.HasMoltenMetal);
  }

  [Fact]
  public void PushMetal_clamps_to_the_cell_capacity() {
    var (world, a, _) = Run();
    // Capacity defaults to 50 units; a 200-unit push tops out there.
    int accepted = a.PushMetal(200, MetalStack(world), world.World);
    Assert.Equal(50, accepted);
    Assert.Equal(50, a.CellAmount);
  }

  [Fact]
  public void DrainMetal_removes_and_empties_the_cell() {
    var (world, a, _) = Run();
    a.PushMetal(30, MetalStack(world), world.World);

    Assert.Equal(20, a.DrainMetal(20));
    Assert.Equal(10, a.CellAmount);

    a.DrainMetal(10);
    Assert.True(a.IsCellEmpty);
    Assert.Equal("", a.CellMetalType);
  }

  [Fact]
  public void A_tick_flows_metal_across_the_connector_and_conserves_the_total() {
    var (world, a, b) = Run();
    a.PushMetal(40, MetalStack(world), world.World);
    Assert.Equal(0, b.CellAmount);

    world.Tick();

    // Exact split, not just "some moved": levelling halves the gap, so 40/0 settles at 20/20. The
    // direction-only form of this assertion passed just as happily when the pair swapped to 0/40.
    Assert.Equal(20, a.CellAmount);
    Assert.Equal(20, b.CellAmount);
    Assert.Equal(40, a.CellAmount + b.CellAmount); // nothing created or destroyed
  }

  [Fact]
  public void A_levelling_tick_never_overshoots_the_midpoint() {
    var (world, a, b) = Run();
    a.PushMetal(40, MetalStack(world), world.World);

    world.Tick();

    // Moving the whole difference inverts the pair - the giver must never end up below the receiver.
    Assert.True(
      a.CellAmount >= b.CellAmount,
      $"giver ended at {a.CellAmount} below receiver at {b.CellAmount}: the pair inverted"
    );
  }

  [Fact]
  public void A_pair_settles_instead_of_sloshing_back_and_forth() {
    var (world, a, b) = Run();
    a.PushMetal(40, MetalStack(world), world.World);

    for (int i = 0; i < 20; i++)
      world.Tick();

    // Twenty ticks in, a two-cell run must be at rest, not still swapping its charge every tick.
    Assert.Equal(20, a.CellAmount);
    Assert.Equal(20, b.CellAmount);
  }

  [Fact]
  public void Different_metals_do_not_mix_across_a_connector() {
    var (world, a, b) = Run();
    a.PushMetal(40, MetalStack(world), world.World);

    // Give the neighbour a different metal so the flow pass must refuse to merge them.
    var brass = new Item { Code = new AssetLocation("game:ingot-brass") };
    b.PushMetal(20, new ItemStack(brass, 1), world.World);

    world.Tick();

    // Neither cell's metal type changed; the iron did not push into the brass cell.
    Assert.Equal("game:ingot-iron", a.CellMetalType);
    Assert.Equal("game:ingot-brass", b.CellMetalType);
  }
}

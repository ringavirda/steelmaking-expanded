using ExpandedLib.Blocks.Networks;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.Tests;
using SteelmakingExpanded.BlockStructures.SmokeStack.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The smoke-stack multiblock is a gas-network sink: each production tick it draws
/// <see cref="SmexValues.SmokestackGasIntakeVolume"/> litres of exhaust off the connected pipe
/// network and vents it, keeping the run from choking the furnace. This spans smex (the sink BE) and
/// ppex (the <see cref="PipeNetwork"/>), so it lives in the integration suite. Covers the IPipeNode
/// reads with and without a network, the structure-gated draw, and the serialization round trip.
/// </summary>
public class SmokeStackTests {
  private static BlockEntitySmokeStack Stack(TestWorld world, BlockPos pos) {
    var be = new BlockEntitySmokeStack {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:smokestack-north",
        70,
        ("orientation", "north")
      ),
    };
    world.Attach(be);
    return be;
  }

  /// <summary>Wires the stack's <c>_system</c> back-reference and marks the multiblock complete.</summary>
  private static void Commission(
    BlockEntitySmokeStack be,
    BlockNetworkModSystem system
  ) {
    ReflectionHelpers.SetField(be, "_system", system);
    ReflectionHelpers.SetProperty(be, "StructureComplete", true);
  }

  private static void ProductionTick(BlockEntitySmokeStack be) =>
    ReflectionHelpers.Invoke(be, "OnProductionTick", 1f);

  #region IPipeNode reads

  [Fact]
  public void Without_a_network_the_node_reports_inert_defaults() {
    var world = new TestWorld();
    var be = Stack(world, new BlockPos(0, 0, 0));

    Assert.Equal("pipe", be.NetworkType);
    Assert.Equal(20f, be.Temperature);
    Assert.Equal("", be.Medium);
    Assert.False(be.IsLiquid);
    Assert.Equal(0f, be.Volume);
    Assert.Equal(0f, be.MaxVolume);
  }

  [Theory]
  [InlineData("north", "north", true)]
  [InlineData("north", "south", false)]
  public void HasConnectorAt_matches_the_orientation_face(
    string orientation,
    string face,
    bool expected
  ) {
    var world = new TestWorld();
    var be = Stack(world, new BlockPos(0, 0, 0));
    be.Orientation = orientation;

    Assert.Equal(expected, be.HasConnectorAt(BlockFacing.FromCode(face)));
  }

  #endregion

  #region Structure-gated draw

  [Fact]
  public void A_complete_stack_draws_exhaust_off_the_network() {
    // A sealed pipe run charged with exhaust; the stack shares the run's first cell.
    var (world, net) = PipeTestWorld.Run(4, capEnds: true);
    net.TryProduceGas(
      200f,
      400f,
      "Exhaust",
      world.Accessor,
      maxOutputPressure: 10f
    );
    float before = net.State!.Volume;

    var be = Stack(world, new BlockPos(0, 0, 0));
    Commission(be, world.Networks);

    ProductionTick(be);

    float drawn = before - net.State!.Volume;
    Assert.Equal(SmexValues.SmokestackGasIntakeVolume, drawn, 1);
  }

  [Fact]
  public void An_incomplete_stack_draws_nothing() {
    var (world, net) = PipeTestWorld.Run(4, capEnds: true);
    net.TryProduceGas(
      200f,
      400f,
      "Exhaust",
      world.Accessor,
      maxOutputPressure: 10f
    );
    float before = net.State!.Volume;

    var be = Stack(world, new BlockPos(0, 0, 0));
    ReflectionHelpers.SetField(be, "_system", world.Networks);
    // StructureComplete deliberately left false.

    ProductionTick(be);

    Assert.Equal(before, net.State!.Volume, 3);
  }

  [Fact]
  public void Draw_is_capped_at_what_the_network_holds() {
    var (world, net) = PipeTestWorld.Run(4, capEnds: true);
    // Less than one intake's worth in the run.
    net.TryProduceGas(
      20f,
      400f,
      "Exhaust",
      world.Accessor,
      maxOutputPressure: 10f
    );

    var be = Stack(world, new BlockPos(0, 0, 0));
    Commission(be, world.Networks);

    ProductionTick(be);

    Assert.Equal(0f, net.State!.Volume, 1); // emptied, not driven negative
  }

  #endregion

  #region Serialization

  [Fact]
  public void Node_state_round_trips_through_the_tree() {
    var world = new TestWorld();
    var src = Stack(world, new BlockPos(0, 0, 0));
    src.Orientation = "north";
    src.PossibleOrientations = ["north", "east"];
    ReflectionHelpers.SetField(src, "_lastConsumedAmount", 42f);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var restored = Stack(world, new BlockPos(0, 0, 0));
    restored.FromTreeAttributes(tree, world.World);

    Assert.Equal("north", restored.Orientation);
    Assert.Equal(new[] { "north", "east" }, restored.PossibleOrientations);
  }

  #endregion
}

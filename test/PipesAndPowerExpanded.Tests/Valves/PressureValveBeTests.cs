using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The pressure-relief valve's player-set gate: stepping it up/down is clamped to
/// [<see cref="BlockEntityPressureValve.MinGatePressure"/>, the valve's material rating], reports
/// whether it actually moved, and the setting persists across a save/reload (defaulting to 1 atm for
/// valves saved before the gate was configurable).
/// </summary>
public class PressureValveBeTests {
  private static BlockPressureValve ValveBlock(string material = "iron") {
    var block = TestBlocks.Configure(
      new BlockPressureValve(),
      $"ppex:pressurevalve-{material}-ns",
      40,
      ("material", material),
      ("type", "pressurevalve"),
      ("orientation", "ns")
    );
    ReflectionHelpers.SetProperty(block, "Type", "pressurevalve");
    ReflectionHelpers.SetProperty(block, "Orientation", "ns");
    return block;
  }

  private static BlockEntityPressureValve Valve(string material = "iron") {
    var world = new TestWorld();
    var be = new BlockEntityPressureValve {
      Pos = new BlockPos(0, 0, 0),
      Block = ValveBlock(material),
    };
    world.Attach(be);
    return be;
  }

  #region Rating

  [Fact]
  public void MaxGatePressure_is_the_blocks_material_burst_rating() {
    var be = Valve("iron");
    Assert.Equal(
      ((BlockPressureValve)be.Block).BurstPressure,
      be.MaxGatePressure
    );
  }

  [Fact]
  public void Steel_valve_has_a_higher_ceiling_than_iron() {
    Assert.True(Valve("steel").MaxGatePressure > Valve("iron").MaxGatePressure);
  }

  #endregion

  #region Adjusting the gate

  [Fact]
  public void Increase_raises_the_gate_by_one_step() {
    var be = Valve();
    float before = be.GatePressure;

    Assert.True(be.AdjustGatePressure(increase: true));
    Assert.Equal(
      before + BlockEntityPressureValve.GatePressureStep,
      be.GatePressure,
      3
    );
  }

  [Fact]
  public void Decrease_lowers_the_gate_by_one_step() {
    var be = Valve();
    float before = be.GatePressure;

    Assert.True(be.AdjustGatePressure(increase: false));
    Assert.Equal(
      before - BlockEntityPressureValve.GatePressureStep,
      be.GatePressure,
      3
    );
  }

  [Fact]
  public void Raising_clamps_at_the_material_ceiling_and_then_reports_no_change() {
    var be = Valve("iron");

    // Step up until it pins at the ceiling.
    for (int i = 0; i < 100 && be.AdjustGatePressure(true); i++) { }

    Assert.Equal(be.MaxGatePressure, be.GatePressure, 3);
    Assert.False(be.AdjustGatePressure(true)); // already at the rail
  }

  [Fact]
  public void Lowering_clamps_at_the_floor_and_then_reports_no_change() {
    var be = Valve();

    for (int i = 0; i < 100 && be.AdjustGatePressure(false); i++) { }

    Assert.Equal(BlockEntityPressureValve.MinGatePressure, be.GatePressure, 3);
    Assert.False(be.AdjustGatePressure(false));
  }

  #endregion

  #region Persistence

  [Fact]
  public void Gate_pressure_round_trips_through_the_tree() {
    var src = Valve();
    src.AdjustGatePressure(true);
    src.AdjustGatePressure(true); // 1 -> 1.5
    float expected = src.GatePressure;

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var world = new TestWorld();
    var restored = new BlockEntityPressureValve {
      Pos = new BlockPos(0, 0, 0),
      Block = ValveBlock(),
    };
    world.Attach(restored);
    restored.FromTreeAttributes(tree, world.World);

    Assert.Equal(expected, restored.GatePressure, 3);
  }

  [Fact]
  public void A_save_without_a_gate_value_defaults_to_one_atm() {
    var world = new TestWorld();
    var be = new BlockEntityPressureValve {
      Pos = new BlockPos(0, 0, 0),
      Block = ValveBlock(),
    };
    world.Attach(be);

    // A tree with the base pipe keys but no "gatePressure" (a pre-feature save).
    var tree = new TreeAttribute();
    tree.SetString("networkType", "pipe");
    tree.SetString("orientation", "ns");
    tree.SetString("possibleOrientations", "[]");
    be.FromTreeAttributes(tree, world.World);

    Assert.Equal(1f, be.GatePressure, 3);
  }

  #endregion

  #region Overflow venting (network-backed)

  /// <summary>
  /// A valve at (0,0,1) facing "ns": its input face (north) butts against a sealed 2-cell pipe run
  /// at (0,0,0)/(0,0,-1) (capped by rock to the north and by the valve block to the south), and its
  /// output face (south) is open air - so any overflow vents to atmosphere. Returns the live input
  /// network so the test can pressurise it.
  /// </summary>
  private static (
    TestWorld world,
    BlockEntityPressureValve valve,
    PipeNetwork inNet
  ) VentRig() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var pipe = PipeTestWorld.MakePipe(orientation: "ns");
    world.Place(new BlockPos(0, 0, -1), pipe);
    world.Place(new BlockPos(0, 0, 0), pipe);

    var valveBlock = ValveBlock();
    var valve = new BlockEntityPressureValve {
      Pos = new BlockPos(0, 0, 1),
      Block = valveBlock,
    };
    world.Place(valve.Pos, valveBlock); // caps the run's south end (non-air)
    world.Attach(valve);
    ReflectionHelpers.SetProperty(
      valve,
      nameof(valve.NetworkSystem),
      world.Networks
    );

    var rock = TestBlocks.Configure(new Block(), "game:rock", 99);
    world.Place(new BlockPos(0, 0, -2), rock); // caps the north end

    world.AddNode(new BlockPos(0, 0, -1), "pipe");
    world.AddNode(new BlockPos(0, 0, 0), "pipe");

    var inNet = (PipeNetwork)world.NetworkAt(new BlockPos(0, 0, 0))!;
    return (world, valve, inNet);
  }

  private static float Tick(BlockEntityPressureValve valve) =>
    (float)ReflectionHelpers.GetField(valve, "_lastVentVolume")!;

  private static void RunTick(BlockEntityPressureValve valve) =>
    ReflectionHelpers.Invoke(valve, "OnTick", 1f);

  [Fact]
  public void Above_the_gate_an_open_face_vents_gas_to_atmosphere() {
    var (world, valve, inNet) = VentRig();

    // MaxVolume = 2 pipes * 30 L = 60; gate is 1 atm, so >60 L is overflow.
    inNet.TryProduceGas(
      120f,
      150f,
      "Steam",
      world.Accessor,
      maxOutputPressure: 10f
    );
    float before = inNet.State!.Volume;

    RunTick(valve);

    Assert.True(Tick(valve) > 0f); // something vented
    Assert.True(inNet.State!.Volume < before); // drawn out of the run
  }

  [Fact]
  public void At_or_below_the_gate_nothing_vents() {
    var (world, valve, inNet) = VentRig();

    // 30 L is below the 60 L gate allowance, so the valve stays shut.
    inNet.TryProduceGas(
      30f,
      150f,
      "Steam",
      world.Accessor,
      maxOutputPressure: 10f
    );

    RunTick(valve);

    Assert.Equal(0f, Tick(valve), 3);
  }

  #endregion
}

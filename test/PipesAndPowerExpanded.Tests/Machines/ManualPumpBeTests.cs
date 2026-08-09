using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using NSubstitute;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using PipesAndPowerExpanded.BlockStructures.ManualPump.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The hand-cranked fluid pump: a no-power way to start a water loop. Right-click-hold drives the
/// crank state (with a watchdog that stops it if the release event is missed), and while cranked it
/// moves standing input water to the output line - the fluid intake on the input network is the real
/// generator. Covers the crank state machine, the watchdog, the water transfer, and persistence.
/// </summary>
public class ManualPumpBeTests {
  private static bool Pumping(BlockEntityManualFluidPump be) =>
    (bool)ReflectionHelpers.GetField(be, "_pumping")!;

  private static bool Drawing(BlockEntityManualFluidPump be) =>
    (bool)ReflectionHelpers.GetField(be, "_drawingWater")!;

  private static BlockEntityManualFluidPump Pump(TestWorld world, BlockPos pos) {
    var be = new BlockEntityManualFluidPump {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "ppex:manualfluidpump-north",
        70,
        ("side", "north")
      ),
    };
    world.Place(pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  #region Crank state machine

  [Fact]
  public void OnPumpStart_then_stop_toggles_the_crank() {
    var world = new TestWorld();
    var be = Pump(world, new BlockPos(0, 8, 0));

    be.OnPumpStart();
    Assert.True(Pumping(be));

    be.OnPumpStop();
    Assert.False(Pumping(be));
  }

  [Fact]
  public void The_watchdog_stops_a_crank_whose_release_event_was_missed() {
    var world = new TestWorld();
    world.World.ElapsedMilliseconds.Returns(5000L);
    var be = Pump(world, new BlockPos(0, 8, 0));

    be.OnPumpStart(); // records _lastStepMs = 5000
    Assert.True(Pumping(be));

    // Time jumps well past the 1200 ms watchdog window with no OnPumpStep refresh.
    world.World.ElapsedMilliseconds.Returns(7000L);
    ReflectionHelpers.Invoke(be, "OnServerTick", 1f);

    Assert.False(Pumping(be));
  }

  #endregion

  #region Water transfer

  /// <summary>
  /// Builds input + output pipe lines on the pump's two connector faces. The input line's single node
  /// is the fluid intake (the generator), primed with water; the output line is an empty pipe run.
  /// </summary>
  private static (PipeNetwork inNet, PipeNetwork outNet) Plumb(
    TestWorld world,
    BlockEntityManualFluidPump pump
  ) {
    int angle = ExOrientation.AngleFromSide("north");
    BlockFacing inFace = ExOrientation.RotateFacing(BlockFacing.SOUTH, angle);
    BlockFacing outFace = ExOrientation.RotateFacing(BlockFacing.NORTH, angle);
    string axis =
      inFace == BlockFacing.NORTH || inFace == BlockFacing.SOUTH ? "ns" : "we";

    // Input node = the intake, presenting a connector back at the pump.
    var intakePos = pump.Pos.AddCopy(inFace);
    var intakeBlock = TestBlocks.Configure(
      new BlockFluidIntake(),
      "ppex:fluidintake",
      60,
      ("orientation", inFace.Opposite.Code[..1])
    );
    ReflectionHelpers.SetProperty(
      intakeBlock,
      "Orientation",
      inFace.Opposite.Code[..1]
    );
    var intake = new BlockEntityFluidIntake {
      Pos = intakePos,
      Block = intakeBlock,
    };
    world.Place(intakePos, intakeBlock, intake);
    world.Attach(intake);
    ReflectionHelpers.SetProperty(intake, nameof(intake.HasWater), true);

    // Output node = a plain pipe presenting a connector back at the pump.
    var outPipe = PipeTestWorld.MakePipe(orientation: axis);
    var outPos = pump.Pos.AddCopy(outFace);
    world.Place(outPos, outPipe);

    world.AddNode(intakePos, "pipe");
    world.AddNode(outPos, "pipe");

    var inNet = (PipeNetwork)world.NetworkAt(intakePos)!;
    var outNet = (PipeNetwork)world.NetworkAt(outPos)!;
    return (inNet, outNet);
  }

  [Fact]
  public void DoWork_moves_standing_input_water_to_the_output_line() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pump = Pump(world, new BlockPos(0, 8, 0));
    var (inNet, outNet) = Plumb(world, pump);
    inNet.TryProduceLiquid(30f, 20f, 1f, world.Accessor); // standing input water

    ReflectionHelpers.Invoke(pump, "DoWork", 1f);

    Assert.True(Drawing(pump)); // an intake is present
    Assert.True(outNet.State!.IsLiquid);
    Assert.True(outNet.State!.Volume > 0f); // water lifted into the output line
  }

  [Fact]
  public void DoWork_with_no_lines_moves_nothing() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pump = Pump(world, new BlockPos(0, 8, 0));

    ReflectionHelpers.Invoke(pump, "DoWork", 1f);

    Assert.False(Drawing(pump));
  }

  #endregion

  #region Persistence

  [Fact]
  public void Run_state_round_trips_through_the_tree() {
    var world = new TestWorld();
    var src = Pump(world, new BlockPos(0, 8, 0));
    ReflectionHelpers.SetField(src, "_pumping", true);
    ReflectionHelpers.SetField(src, "_drawingWater", true);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Pump(world, new BlockPos(0, 8, 0));
    dst.FromTreeAttributes(tree, world.World);

    Assert.True(Pumping(dst));
    Assert.True(Drawing(dst));
  }

  #endregion
}

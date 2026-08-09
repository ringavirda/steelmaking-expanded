using System;
using System.Collections;
using System.IO;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.MpPump.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.MpPump.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The mechanical (walking-beam) fluid pump: the axle-driven way to fill a boiler whose fire is
/// out. It shares the manual pump's transfer model - the fluid intake on the source line is the
/// generator, not the pump - but sizes its stroke from the mechanical network's speed and commands
/// a fixed delivery head however fast the shaft runs. Covers the speed-response curve, where the
/// rated flow sits among the other pumps, the delivery head, the axle coupling and the two ports.
/// </summary>
public class MpFluidPumpTests {
  /// <summary>The shipped block def, whose <c>attributes</c> carry the two port offsets.</summary>
  private const string Def =
    "src/PipesAndPowerExpanded/assets/ppex/blocktypes/mpfluidpump.json";

  private static bool Drawing(BlockEntityMpFluidPump be) =>
    (bool)ReflectionHelpers.GetField(be, "_drawingWater")!;

  /// <summary>
  /// A pump block primed with the shipped def's <c>attributes</c>, so the generated offset
  /// accessors read the same node the game hands them (the headless harness has no asset
  /// pipeline, so the def is loaded by hand).
  /// </summary>
  private static BlockMpFluidPump PumpBlock(string side = "north") {
    var block = TestBlocks.Configure(
      new BlockMpFluidPump(),
      $"ppex:mpfluidpump-{side}",
      80,
      ("side", side)
    );
    block.Attributes = new JsonObject(Attributes(Def));
    return block;
  }

  private static JToken Attributes(string repoRelativePath) {
    string full = Path.Combine(
      RepoRoot(),
      repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
    );
    Assert.True(File.Exists(full), $"missing asset: {full}");
    JObject def = JObject.Parse(File.ReadAllText(full));
    JToken? attributes = def["attributes"];
    Assert.True(attributes != null, $"{repoRelativePath} has no attributes");
    return attributes!;
  }

  private static string RepoRoot() {
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (
      dir != null
      && !File.Exists(Path.Combine(dir.FullName, "VintageStory.sln"))
    )
      dir = dir.Parent;
    Assert.True(dir != null, "could not locate the repository root");
    return dir!.FullName;
  }

  private static BlockEntityMpFluidPump Pump(
    TestWorld world,
    BlockPos pos,
    string side = "north"
  ) {
    var be = new BlockEntityMpFluidPump { Pos = pos, Block = PumpBlock(side) };
    world.Place(pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  /// <summary>
  /// Attaches a drive behaviour to <paramref name="pump"/>, bound to a mechanical network turning
  /// at <paramref name="speed"/>, or to no network at all when it is <c>null</c>. The headless
  /// harness skips the wiring the game does at chunk load, so the behaviour list and the network
  /// field are primed directly.
  /// </summary>
  private static BEBehaviorMpPumpDrive Drive(
    BlockEntity pump,
    float? speed = null
  ) {
    var drive = new BEBehaviorMpPumpDrive(pump);
    if (speed is { } s)
      ReflectionHelpers.SetField(
        drive,
        "network",
        new MechanicalNetwork { Speed = s }
      );
    ((IList)ReflectionHelpers.GetField(pump, "Behaviors")!).Add(drive);
    return drive;
  }

  private static bool HasMechConnector(
    BlockMpFluidPump block,
    TestWorld world,
    BlockPos pos,
    BlockFacing face
  ) =>
#if GAME_GE_1_22
    block.HasMechPowerConnectorAt(world.World, pos, face, null!);
#else
    block.HasMechPowerConnectorAt(world.World, pos, face);
#endif

  #region Speed response

  [Fact]
  public void At_or_below_the_minimum_speed_the_beam_moves_nothing() {
    float min = PpexValues.MpPumpMinSpeed;

    Assert.Equal(0f, BlockEntityMpFluidPump.SpeedFraction(0f), 4);
    Assert.Equal(0f, BlockEntityMpFluidPump.SpeedFraction(min * 0.5f), 4);
    Assert.Equal(0f, BlockEntityMpFluidPump.SpeedFraction(min), 4);
  }

  [Fact]
  public void At_or_above_the_maximum_speed_the_beam_moves_its_full_rate() {
    float max = PpexValues.MpPumpMaxSpeed;

    Assert.Equal(1f, BlockEntityMpFluidPump.SpeedFraction(max), 4);
    Assert.Equal(1f, BlockEntityMpFluidPump.SpeedFraction(max * 2f), 4);
    Assert.Equal(1f, BlockEntityMpFluidPump.SpeedFraction(1000f), 4);
  }

  [Fact]
  public void The_response_rises_linearly_across_the_band() {
    float min = PpexValues.MpPumpMinSpeed;
    float band = PpexValues.MpPumpMaxSpeed - min;

    Assert.Equal(
      0.25f,
      BlockEntityMpFluidPump.SpeedFraction(min + band * 0.25f),
      4
    );
    Assert.Equal(
      0.5f,
      BlockEntityMpFluidPump.SpeedFraction(min + band * 0.5f),
      4
    );
    Assert.Equal(
      0.75f,
      BlockEntityMpFluidPump.SpeedFraction(min + band * 0.75f),
      4
    );
  }

  [Fact]
  public void Retuning_the_band_moves_the_response_curve() {
    float min = PpexValues.MpPumpMinSpeed;
    float max = PpexValues.MpPumpMaxSpeed;
    try {
      PpexValues.Edit(c => {
        c.MpPumpMinSpeed = 2f;
        c.MpPumpMaxSpeed = 4f;
      });

      Assert.Equal(0f, BlockEntityMpFluidPump.SpeedFraction(2f), 4);
      Assert.Equal(0.5f, BlockEntityMpFluidPump.SpeedFraction(3f), 4);
      Assert.Equal(1f, BlockEntityMpFluidPump.SpeedFraction(4f), 4);
      // The speed that ran the old band at full rate now sits under the new floor.
      Assert.Equal(0f, BlockEntityMpFluidPump.SpeedFraction(max), 4);
    } finally {
      PpexValues.Edit(c => {
        c.MpPumpMinSpeed = min;
        c.MpPumpMaxSpeed = max;
      });
    }
  }

  [Fact]
  public void A_collapsed_band_is_all_or_nothing() {
    float min = PpexValues.MpPumpMinSpeed;
    float max = PpexValues.MpPumpMaxSpeed;
    try {
      PpexValues.Edit(c => {
        c.MpPumpMinSpeed = 1f;
        c.MpPumpMaxSpeed = 1f;
      });

      Assert.Equal(0f, BlockEntityMpFluidPump.SpeedFraction(1f), 4);
      Assert.Equal(1f, BlockEntityMpFluidPump.SpeedFraction(1.01f), 4);
    } finally {
      PpexValues.Edit(c => {
        c.MpPumpMinSpeed = min;
        c.MpPumpMaxSpeed = max;
      });
    }
  }

  #endregion

  #region Rate placement

  [Fact]
  public void The_rated_flow_sits_between_the_hand_crank_and_the_engine_pump() {
    // The ordering is the machine's design intent: a mechanical pump beats hand cranking and
    // loses to steam. The engine pump's realised rate is its nominal figure times its throughput
    // calibration times the engine's power, so the weakest engine setting is the ceiling the
    // mechanical pump has to stay under.
    float weakestEnginePower = Math.Min(
      PpexValues.WattEngineMaxPower,
      PpexValues.CornishEnginePowerLow
    );
    float weakestEnginePump =
      PpexValues.PumpWaterPerSecond
      * BlockEntityEngineFluidPump.ThroughputScale
      * weakestEnginePower;

    Assert.True(
      PpexValues.ManualPumpWaterPerSecond < PpexValues.MpPumpWaterPerSecond,
      "the mechanical pump should beat the hand crank"
    );
    Assert.True(
      PpexValues.MpPumpWaterPerSecond < weakestEnginePump,
      "the mechanical pump should lose to the weakest engine pump"
    );
  }

  #endregion

  #region Water transfer and delivery head

  /// <summary>
  /// Builds the two lines the pump works between, in the north orientation: a source line hanging
  /// under the far, low filler cell (a vertical riser plus the fluid intake that generates its
  /// water) and an empty delivery main across the output face of the far, high cell. Both ports
  /// sit on filler cells, so the lines attach a cell out from the pump's own position.
  /// </summary>
  private static (PipeNetwork source, PipeNetwork delivery) Plumb(
    TestWorld world,
    BlockEntityMpFluidPump pump,
    bool withIntake = true
  ) {
    var block = (BlockMpFluidPump)pump.Block;

    // Source riser: presents a connector up at the source port cell, and north at the intake.
    BlockPos riserPos = block
      .SourceWorldPos(pump.Pos)
      .AddCopy(BlockMpFluidPump.SourceFace);
    world.Place(riserPos, PipeTestWorld.MakePipe(id: 1, orientation: "un"));
    world.AddNode(riserPos, "pipe");

    if (withIntake) {
      BlockPos intakePos = riserPos.AddCopy(BlockFacing.NORTH);
      var intakeBlock = TestBlocks.Configure(
        new BlockFluidIntake(),
        "ppex:fluidintake",
        60,
        ("orientation", "s")
      );
      ReflectionHelpers.SetProperty(intakeBlock, "Orientation", "s");
      var intake = new BlockEntityFluidIntake {
        Pos = intakePos,
        Block = intakeBlock,
      };
      world.Place(intakePos, intakeBlock, intake);
      world.Attach(intake);
      ReflectionHelpers.SetProperty(intake, nameof(intake.HasWater), true);
      world.AddNode(intakePos, "pipe");
    }

    // Delivery main: an empty run across the output face of the high port cell.
    BlockPos mainPos = block.OutletWorldPos(pump.Pos).AddCopy(block.OutputFace);
    world.Place(mainPos, PipeTestWorld.MakePipe(id: 2, orientation: "ns"));
    world.AddNode(mainPos, "pipe");

    return (
      (PipeNetwork)world.NetworkAt(riserPos)!,
      (PipeNetwork)world.NetworkAt(mainPos)!
    );
  }

  private static (
    TestWorld world,
    BlockEntityMpFluidPump pump,
    PipeNetwork source,
    PipeNetwork delivery
  ) Rig(bool withIntake = true) {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pump = Pump(world, new BlockPos(0, 8, 0));
    var (source, delivery) = Plumb(world, pump, withIntake);
    source.TryProduceLiquid(30f, 20f, 1f, world.Accessor); // standing source water
    return (world, pump, source, delivery);
  }

  [Fact]
  public void DoWork_lifts_source_water_into_the_delivery_line() {
    var (_, pump, _, delivery) = Rig();

    float moved = pump.DoWork(PpexValues.MpPumpMaxSpeed, 1f);

    Assert.True(Drawing(pump)); // an intake is present on the source line
    Assert.Equal(PpexValues.MpPumpWaterPerSecond, moved, 3);
    Assert.True(delivery.State!.IsLiquid);
    Assert.Equal(PpexValues.MpPumpWaterPerSecond, delivery.State!.Volume, 3);
  }

  [Fact]
  public void The_delivery_head_is_the_same_at_any_axle_speed() {
    float band = PpexValues.MpPumpMaxSpeed - PpexValues.MpPumpMinSpeed;
    float slow = PpexValues.MpPumpMinSpeed + band * 0.25f;
    float fast = PpexValues.MpPumpMaxSpeed * 2f;

    float slowHead = DeliveredHead(slow, out float slowMoved);
    float fastHead = DeliveredHead(fast, out float fastMoved);

    // The beam lifts the same column however fast it runs, so the commanded head is fixed even
    // though the volume per second is not.
    Assert.Equal(PpexValues.MpPumpDeliveryPressure, slowHead, 4);
    Assert.Equal(PpexValues.MpPumpDeliveryPressure, fastHead, 4);
    Assert.True(
      fastMoved > slowMoved,
      "a faster shaft should move more water per second"
    );
  }

  /// <summary>Runs one second of work at <paramref name="speed"/> and reports the head the
  /// delivery line was fed at, along with the litres that landed.</summary>
  private static float DeliveredHead(float speed, out float moved) {
    var (_, pump, _, delivery) = Rig();
    moved = pump.DoWork(speed, 1f);
    return delivery.State?.FeedPressure ?? 0f;
  }

  [Fact]
  public void Below_the_minimum_speed_no_water_moves() {
    var (_, pump, _, delivery) = Rig();

    float moved = pump.DoWork(PpexValues.MpPumpMinSpeed, 1f);

    Assert.Equal(0f, moved, 4);
    Assert.False(Drawing(pump));
    Assert.Null(delivery.State);
  }

  [Fact]
  public void With_no_intake_on_the_source_line_the_beam_moves_nothing() {
    // The pump is a transfer device, not a generator: standing water with no intake behind it
    // stays where it is.
    var (_, pump, _, delivery) = Rig(withIntake: false);

    float moved = pump.DoWork(PpexValues.MpPumpMaxSpeed, 1f);

    Assert.Equal(0f, moved, 4);
    Assert.False(Drawing(pump));
    Assert.Null(delivery.State);
  }

  #endregion

  #region Axle coupling

  [Theory]
  [InlineData("north", "east")]
  [InlineData("east", "south")]
  [InlineData("south", "west")]
  [InlineData("west", "north")]
  public void The_drive_face_rotates_with_the_side_variant(
    string side,
    string face
  ) {
    Assert.Equal(BlockFacing.FromCode(face), PumpBlock(side).DriveFace);
  }

  [Fact]
  public void The_axle_couples_on_both_ends_of_the_drive_line() {
    var world = new TestWorld();
    var pos = new BlockPos(0, 8, 0);
    var block = PumpBlock();

    Assert.True(HasMechConnector(block, world, pos, block.DriveFace));
    Assert.True(HasMechConnector(block, world, pos, block.DriveFace.Opposite));
  }

  [Fact]
  public void The_faces_off_the_drive_line_take_no_axle() {
    var world = new TestWorld();
    var pos = new BlockPos(0, 8, 0);
    var block = PumpBlock(); // drive line runs east-west

    Assert.False(HasMechConnector(block, world, pos, BlockFacing.NORTH));
    Assert.False(HasMechConnector(block, world, pos, BlockFacing.SOUTH));
    Assert.False(HasMechConnector(block, world, pos, BlockFacing.UP));
    Assert.False(HasMechConnector(block, world, pos, BlockFacing.DOWN));
  }

  [Theory]
  [InlineData("north", "west")]
  [InlineData("east", "north")]
  public void The_drive_behaviour_discovers_along_the_drive_line(
    string side,
    string discovery
  ) {
    var world = new TestWorld();
    var pump = Pump(world, new BlockPos(0, 8, 0), side);
    var drive = Drive(pump);

    drive.SetOrientations();

    Assert.Equal(
      BlockFacing.FromCode(discovery),
      drive.OutFacingForNetworkDiscovery
    );
    // One sign per axis, so the two ends of the shaft cannot counter-rotate.
    Assert.Equal(
      drive.OutFacingForNetworkDiscovery.Axis == EnumAxis.X
        ? new[] { -1, 0, 0 }
        : new[] { 0, 0, -1 },
      drive.AxisSign
    );
    // The discovery face is one end of the same line the block accepts axles on.
    var block = (BlockMpFluidPump)pump.Block;
    Assert.True(
      HasMechConnector(
        block,
        world,
        pump.Pos,
        drive.OutFacingForNetworkDiscovery
      )
    );
  }

  [Fact]
  public void Drive_speed_is_the_networks_speed_without_its_sign() {
    var world = new TestWorld();
    var pump = Pump(world, new BlockPos(0, 8, 0));

    // A shaft driven from the other end reports a negative network speed.
    var drive = Drive(pump, -1.2f);

    Assert.True(drive.IsTurning);
    Assert.Equal(1.2f, drive.DriveSpeed, 4);
  }

  [Fact]
  public void A_stalled_or_uncoupled_shaft_is_not_turning() {
    var world = new TestWorld();
    var stalled = Drive(Pump(world, new BlockPos(0, 8, 0)), 0f);
    var bare = Drive(Pump(world, new BlockPos(4, 8, 0)));

    Assert.False(stalled.IsTurning);
    Assert.Equal(0f, stalled.DriveSpeed, 4);
    Assert.False(bare.IsTurning);
    Assert.Equal(0f, bare.DriveSpeed, 4);
  }

  [Fact]
  public void Resistance_is_the_walking_beams_load() {
    var world = new TestWorld();
    var drive = Drive(Pump(world, new BlockPos(0, 8, 0)));

    Assert.Equal(
      BEBehaviorMpPumpDrive.PumpResistance,
      drive.GetResistance(),
      4
    );
  }

  #endregion

  #region Port layout

  [Fact]
  public void The_source_port_hangs_under_the_far_low_filler_cell() {
    var block = PumpBlock();
    var pos = new BlockPos(4, 8, 4);

    Assert.Equal(BlockFacing.DOWN, BlockMpFluidPump.SourceFace);
    Assert.Equal(pos.AddCopy(0, 0, 2), block.SourceWorldPos(pos));
  }

  [Fact]
  public void The_delivery_port_leaves_the_far_high_filler_cell() {
    var block = PumpBlock();
    var pos = new BlockPos(4, 8, 4);

    Assert.Equal(BlockFacing.SOUTH, block.OutputFace);
    Assert.Equal(pos.AddCopy(0, 1, 2), block.OutletWorldPos(pos));
  }

  [Theory]
  [InlineData("north", 0, 2, "south")]
  [InlineData("west", 2, 0, "east")]
  [InlineData("south", 0, -2, "north")]
  [InlineData("east", -2, 0, "west")]
  public void The_ports_rotate_with_the_side_variant(
    string side,
    int dx,
    int dz,
    string outFace
  ) {
    var block = PumpBlock(side);
    var pos = new BlockPos(4, 8, 4);

    Assert.Equal(pos.AddCopy(dx, 0, dz), block.SourceWorldPos(pos));
    Assert.Equal(pos.AddCopy(dx, 1, dz), block.OutletWorldPos(pos));
    Assert.Equal(BlockFacing.FromCode(outFace), block.OutputFace);
  }

  #endregion

  #region Persistence

  [Fact]
  public void Run_state_round_trips_through_the_tree() {
    var world = new TestWorld();
    var src = Pump(world, new BlockPos(0, 8, 0));
    ReflectionHelpers.SetField(src, "_lastSpeed", PpexValues.MpPumpMaxSpeed);
    ReflectionHelpers.SetField(src, "_drawingWater", true);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Pump(world, new BlockPos(0, 8, 0));
    dst.FromTreeAttributes(tree, world.World);

    Assert.Equal(1f, dst.OutputFraction, 4);
    Assert.True(Drawing(dst));
  }

  #endregion
}

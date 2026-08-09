using System;
using System.Collections;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks.Structures;
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
  public void Throughput_is_a_straight_proportion_of_axle_speed() {
    // A displacement pump moves what its plunger sweeps, so the rate follows the shaft with no
    // threshold and no ceiling. The band this replaced had its full-rate point at speed 1.5, which
    // is the MP generator's UNLOADED top speed - unreachable once the pump itself loads the shaft,
    // so the whole usable range was squeezed into the bottom of the ramp and a Watt engine read as
    // single-digit percent.
    float rate = PpexValues.MpPumpLitresPerSpeed;

    Assert.Equal(0f, BlockEntityMpFluidPump.OutputAt(0f), 4);
    Assert.Equal(rate * 0.25f, BlockEntityMpFluidPump.OutputAt(0.25f), 4);
    Assert.Equal(rate, BlockEntityMpFluidPump.OutputAt(1f), 4);
    Assert.Equal(rate * 3f, BlockEntityMpFluidPump.OutputAt(3f), 4);
  }

  [Fact]
  public void A_barely_turning_shaft_still_moves_water() {
    // The point of dropping the floor: a slow line shaft is meant to trickle, not to deliver
    // nothing at all.
    Assert.True(BlockEntityMpFluidPump.OutputAt(0.05f) > 0f);
  }

  [Fact]
  public void A_reversed_or_stopped_shaft_moves_nothing() {
    Assert.Equal(0f, BlockEntityMpFluidPump.OutputAt(0f), 4);
    Assert.Equal(0f, BlockEntityMpFluidPump.OutputAt(-1f), 4);
  }

  [Fact]
  public void Retuning_the_displacement_scales_the_whole_curve() {
    float rate = PpexValues.MpPumpLitresPerSpeed;
    try {
      PpexValues.Edit(c => c.MpPumpLitresPerSpeed = 20f);

      Assert.Equal(20f, BlockEntityMpFluidPump.OutputAt(1f), 4);
      Assert.Equal(10f, BlockEntityMpFluidPump.OutputAt(0.5f), 4);
    } finally {
      PpexValues.Edit(c => c.MpPumpLitresPerSpeed = rate);
    }
  }

  [Fact]
  public void The_shaft_load_carries_the_delivery_head() {
    // This is where drive size enters: the beam's torque draw is set by the column it lifts, so the
    // network settles at speed = drive budget / this load. Raise the head and every drive turns the
    // pump slower, which is the rate falling off - not a band redrawn on speed.
    float head = PpexValues.MpPumpDeliveryPressure;
    float atHead = BEBehaviorMpPumpDrive.PumpResistance;
    try {
      PpexValues.Edit(c => c.MpPumpDeliveryPressure = head * 2f);

      Assert.True(BEBehaviorMpPumpDrive.PumpResistance > atHead);
      Assert.Equal(
        PpexValues.MpPumpBaseLoad + PpexValues.MpPumpLoadPerAtm * head * 2f,
        BEBehaviorMpPumpDrive.PumpResistance,
        4
      );
    } finally {
      PpexValues.Edit(c => c.MpPumpDeliveryPressure = head);
    }
  }

  #endregion

  #region Rate placement

  [Fact]
  public void At_a_waterwheel_the_flow_sits_between_the_hand_crank_and_the_engine_pump() {
    // The ordering is the machine's design intent, and it is anchored at the WATERWHEEL - the drive
    // this pump exists for, the pre-steam way to fill a boiler whose fire is out. Driven from an
    // engine instead it runs well past the engine pump, which is intended: that costs an engine, a
    // generator and a line shaft where the engine pump bolts straight onto the engine.
    // A vanilla wheel is speed-seeking, settling at min(0.3, flowRate) - load/flowRate, and cannot
    // pass 0.3 even unloaded.
    float weakestEnginePower = Math.Min(
      PpexValues.WattEngineMaxPower,
      PpexValues.CornishEnginePowerLow
    );
    float weakestEnginePump =
      PpexValues.PumpWaterPerSecond * weakestEnginePower;

    float wheelSpeed = Math.Max(
      0f,
      Math.Min(0.3f, 1f) - BEBehaviorMpPumpDrive.PumpResistance / 1f
    );
    float atWaterwheel = BlockEntityMpFluidPump.OutputAt(wheelSpeed);

    Assert.True(
      wheelSpeed > 0f,
      "a waterwheel must be able to turn the pump at all"
    );

    Assert.True(
      PpexValues.ManualPumpWaterPerSecond < atWaterwheel,
      "the mechanical pump should beat the hand crank"
    );
    Assert.True(
      atWaterwheel < weakestEnginePump,
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

    float moved = pump.DoWork(1f, 1f);

    Assert.True(Drawing(pump)); // an intake is present on the source line
    Assert.Equal(BlockEntityMpFluidPump.OutputAt(1f), moved, 3);
    Assert.True(delivery.State!.IsLiquid);
    Assert.Equal(
      BlockEntityMpFluidPump.OutputAt(1f),
      delivery.State!.Volume,
      3
    );
  }

  [Fact]
  public void The_delivery_head_is_the_same_at_any_axle_speed() {
    // Two speeds an order of magnitude apart: the plunger lifts the same column either way, only
    // more often, so the head must not move with the shaft even though the volume does.
    float slow = 0.2f;
    float fast = 3f;

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
  public void A_stopped_shaft_moves_no_water() {
    // There is no minimum speed any more - only a stopped shaft does nothing. A slow one trickles,
    // which is checked in the speed-response region.
    var (_, pump, _, delivery) = Rig();

    float moved = pump.DoWork(0f, 1f);

    Assert.Equal(0f, moved, 4);
    Assert.False(Drawing(pump));
    Assert.Null(delivery.State);
  }

  [Fact]
  public void With_no_intake_on_the_source_line_the_beam_moves_nothing() {
    // The pump is a transfer device, not a generator: standing water with no intake behind it
    // stays where it is.
    var (_, pump, _, delivery) = Rig(withIntake: false);

    float moved = pump.DoWork(1f, 1f);

    Assert.Equal(0f, moved, 4);
    Assert.False(Drawing(pump));
    Assert.Null(delivery.State);
  }

  #endregion

  #region Axle coupling

  // The drive face must equal the behaviour's discovery face, not merely share its axis. Both
  // tables below therefore read the same: while DriveFace was derived off the un-offset angle it
  // came out as the OPPOSITE of the discovery face, and nothing caught it, because
  // HasMechPowerConnectorAt accepts a face or its opposite so an axle still coupled in tests.
  [Theory]
  [InlineData("north", "west")]
  [InlineData("east", "north")]
  [InlineData("south", "east")]
  [InlineData("west", "south")]
  public void The_drive_face_rotates_with_the_side_variant(
    string side,
    string face
  ) {
    Assert.Equal(BlockFacing.FromCode(face), PumpBlock(side).DriveFace);
  }

  [Theory]
  [InlineData("north")]
  [InlineData("east")]
  [InlineData("south")]
  [InlineData("west")]
  public void The_drive_face_is_exactly_the_discovery_face(string side) {
    var world = new TestWorld();
    var pump = Pump(world, new BlockPos(0, 8, 0), side);
    var drive = Drive(pump);

    drive.SetOrientations();

    Assert.Equal(
      ((BlockMpFluidPump)pump.Block).DriveFace,
      drive.OutFacingForNetworkDiscovery
    );
  }

  [Fact]
  public void The_axle_couples_on_the_drive_face_only() {
    var world = new TestWorld();
    var pos = new BlockPos(0, 8, 0);
    var block = PumpBlock();

    Assert.True(HasMechConnector(block, world, pos, block.DriveFace));
    // Not the far side: the pump is the end of a line, not a length of shafting. Accepting the
    // opposite face made it a passthrough that carried rotation straight out the other side.
    Assert.False(HasMechConnector(block, world, pos, block.DriveFace.Opposite));
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

  /// <summary>
  /// A network's speed is expressed in the frame of whichever node seeded it, and which node that is
  /// depends on chunk load order. Only <c>GearedRatio</c> converts it to this shaft's own speed, so a
  /// pump behind a gear train that read the raw figure reported the drive's speed on one load and its
  /// own on the next.
  /// </summary>
  [Fact]
  public void Drive_speed_is_geared_into_this_shafts_own_frame() {
    var world = new TestWorld();
    var pump = Pump(world, new BlockPos(0, 8, 0));

    var drive = Drive(pump, 0.3f);
    drive.GearedRatio = 5.5f;

    Assert.Equal(1.65f, drive.DriveSpeed, 4);
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

    // The body is raised away from the player, so local +z lands at world -z for a north pump.
    Assert.Equal(BlockFacing.DOWN, BlockMpFluidPump.SourceFace);
    Assert.Equal(pos.AddCopy(0, 0, -2), block.SourceWorldPos(pos));
  }

  [Fact]
  public void The_delivery_port_leaves_the_far_high_filler_cell() {
    var block = PumpBlock();
    var pos = new BlockPos(4, 8, 4);

    // The delivery looks back along the body, into the notch of the L - the far face has no cell
    // to run a main into, the notch does.
    Assert.Equal(BlockFacing.SOUTH, block.OutputFace);
    Assert.Equal(pos.AddCopy(0, 1, -2), block.OutletWorldPos(pos));
  }

  [Fact]
  public void Both_port_cells_accept_an_attached_block() {
    // A port cell exists to be attached to, so its filler must opt into allowAttach. Without it the
    // filler swallows the click and the line can only be sneak-placed onto the port - which reads
    // as the connector not working at all.
    var block = PumpBlock();
    var pos = new BlockPos(4, 8, 4);
    var cells = StructureFillers.FootprintCells(
      block,
      pos,
      block.StructureAngle
    );

    foreach (
      BlockPos port in new[]
      {
        block.SourceWorldPos(pos),
        block.OutletWorldPos(pos),
      }
    )
      Assert.True(
        cells.Single(c => c.Pos.Equals(port)).AllowAttach,
        $"the filler at {port} carries a port, so it must allow attachment"
      );
  }

  // The cells run away from the player (a north pump reserves world -z) while the delivery faces
  // back down the body, so the port face is the opposite of the side variant.
  [Theory]
  [InlineData("north", 0, -2, "south")]
  [InlineData("west", -2, 0, "east")]
  [InlineData("south", 0, 2, "north")]
  [InlineData("east", 2, 0, "west")]
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
    ReflectionHelpers.SetField(src, "_lastSpeed", 1f);
    ReflectionHelpers.SetField(src, "_drawingWater", true);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Pump(world, new BlockPos(0, 8, 0));
    dst.FromTreeAttributes(tree, world.World);

    Assert.Equal(BlockEntityMpFluidPump.OutputAt(1f), dst.OutputPerSecond, 4);
    Assert.True(Drawing(dst));
  }

  #endregion
}

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
/// The fluid intake is the water generator: it draws from the pond below into its own pipe network
/// only when the cube beneath is full water and no other intake crowds it. Covers the water scan, the
/// <c>CanIntake</c> gate, the network-feeding <c>ProduceWater</c> draw, and the state round trip.
/// </summary>
public class FluidIntakeBeTests {
  private static Block WaterBlock(TestWorld world) {
    var water = TestBlocks.Configure(new Block(), "game:water-still-7", 200);
    water.LiquidCode = "water";
    world.Register(water);
    return water;
  }

  /// <summary>An intake at the origin, registered in its own pipe network.</summary>
  private static (
    TestWorld world,
    BlockEntityFluidIntake intake,
    PipeNetwork net
  ) Rig() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var block = TestBlocks.Configure(new Block(), "ppex:fluidintake", 60);
    var intake = new BlockEntityFluidIntake {
      Pos = new BlockPos(0, 8, 0),
      Block = block,
    };
    world.Place(intake.Pos, block, intake);
    world.Attach(intake);
    ReflectionHelpers.SetProperty(
      intake,
      nameof(intake.NetworkSystem),
      world.Networks
    );
    world.AddNode(intake.Pos, "pipe");

    var net = (PipeNetwork)world.NetworkAt(intake.Pos)!;
    return (world, intake, net);
  }

  /// <summary>Fills the depth³ cube below the intake with water so the scan passes.</summary>
  private static void FloodBelow(TestWorld world, BlockPos pos) {
    var water = WaterBlock(world);
    int depth = PpexValues.FluidIntakeWaterDepth;
    int half = depth / 2;
    for (int dx = -half; dx <= half; dx++)
      for (int dy = -1; dy >= -depth; dy--)
        for (int dz = -half; dz <= half; dz++)
          world.Place(new BlockPos(pos.X + dx, pos.Y + dy, pos.Z + dz), water);
  }

  #region Water scan + CanIntake

  [Fact]
  public void Scans_a_full_water_cube_as_intakeable() {
    var (world, intake, _) = Rig();
    FloodBelow(world, intake.Pos);

    ReflectionHelpers.Invoke(intake, "Rescan", 0f);

    Assert.True(intake.HasWater);
    Assert.False(intake.Crowded);
    Assert.True(intake.CanIntake);
  }

  [Fact]
  public void A_dry_cube_below_blocks_the_intake() {
    var (world, intake, _) = Rig(); // nothing flooded -> air below

    ReflectionHelpers.Invoke(intake, "Rescan", 0f);

    Assert.False(intake.HasWater);
    Assert.False(intake.CanIntake);
  }

  [Fact]
  public void A_nearby_intake_marks_this_one_crowded() {
    var (world, intake, _) = Rig();
    FloodBelow(world, intake.Pos);
    // A second intake block two cells away, inside the exclusion range.
    world.Place(
      new BlockPos(intake.Pos.X + 2, intake.Pos.Y, intake.Pos.Z),
      TestBlocks.Configure(new BlockFluidIntake(), "ppex:fluidintake", 61)
    );

    ReflectionHelpers.Invoke(intake, "Rescan", 0f);

    Assert.True(intake.HasWater);
    Assert.True(intake.Crowded);
    Assert.False(intake.CanIntake); // water present, but crowded out
  }

  #endregion

  #region ProduceWater

  [Fact]
  public void ProduceWater_feeds_the_network_when_intakeable() {
    var (world, intake, net) = Rig();
    FloodBelow(world, intake.Pos);
    ReflectionHelpers.Invoke(intake, "Rescan", 0f);

    float produced = intake.ProduceWater(20f, 12f, world.Accessor);

    Assert.True(produced > 0f);
    Assert.True(net.State!.IsLiquid);
    Assert.True(net.State!.Volume > 0f);
  }

  [Fact]
  public void ProduceWater_draws_nothing_when_not_intakeable() {
    var (world, intake, _) = Rig(); // dry -> CanIntake false

    Assert.Equal(0f, intake.ProduceWater(20f, 12f, world.Accessor));
  }

  #endregion

  #region Serialization

  [Fact]
  public void Scan_state_round_trips_through_the_tree() {
    var (world, intake, _) = Rig();
    FloodBelow(world, intake.Pos);
    ReflectionHelpers.Invoke(intake, "Rescan", 0f);

    var tree = new TreeAttribute();
    intake.ToTreeAttributes(tree);

    var restored = new BlockEntityFluidIntake {
      Pos = intake.Pos.Copy(),
      Block = intake.Block,
    };
    world.Attach(restored);
    restored.FromTreeAttributes(tree, world.World);

    Assert.True(restored.HasWater);
  }

  #endregion
}

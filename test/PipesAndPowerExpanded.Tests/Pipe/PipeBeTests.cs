using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The pipe block entity: how a network broadcast lands in its cached display fields, how those
/// fields and the restore-on-load network state round-trip through the save tree, and the live
/// path where a real <see cref="PipeNetwork"/> pushes state into placed pipe entities.
/// </summary>
public class PipeBeTests {
  private static (TestWorld world, BlockEntityPipe be) NewPipe(
    BlockPos? at = null
  ) {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pos = at ?? new BlockPos(0, 0, 0);
    var block = PipeTestWorld.MakePipe();
    var be = new BlockEntityPipe { Pos = pos, Block = block };
    world.Place(pos, block, be);
    world.Attach(be);
    return (world, be);
  }

  private static PipeNetworkState State(
    float vol = 300f,
    float max = 900f,
    float temp = 150f,
    string medium = "Steam",
    float flow = 5f,
    float pressure = 2f,
    int openings = 0
  ) =>
    new() {
      Volume = vol,
      MaxVolume = max,
      Temperature = temp,
      MediumType = medium,
      FlowRate = flow,
      Pressure = pressure,
      OpeningsCount = openings,
    };

  [Fact]
  public void OnNetworkUpdate_caches_the_broadcast_state() {
    var (_, be) = NewPipe();

    be.OnNetworkUpdate(State());

    Assert.Equal(300f, be.Volume, 3);
    Assert.Equal(900f, be.MaxVolume, 3);
    Assert.Equal(2f, be.Pressure, 3);
    Assert.Equal(150f, be.Temperature, 3);
    Assert.Equal("Steam", be.Medium);
    Assert.False(be.IsLiquid);
  }

  [Fact]
  public void OnNetworkUpdate_with_water_medium_reads_as_liquid() {
    var (_, be) = NewPipe();
    be.OnNetworkUpdate(State(medium: "Water"));
    Assert.True(be.IsLiquid);
  }

  [Fact]
  public void OnNetworkUpdate_null_clears_to_neutral_display() {
    var (_, be) = NewPipe();
    be.OnNetworkUpdate(State());

    be.OnNetworkUpdate(null);

    Assert.Equal(20f, be.Temperature, 3); // neutral default
    Assert.Equal("", be.Medium);
    Assert.Equal(0f, be.Pressure, 3);
  }

  [Fact]
  public void Meaningful_state_is_cached_for_restore_empty_state_is_not() {
    var (_, be) = NewPipe();

    be.OnNetworkUpdate(State(vol: 300f, flow: 0f));
    Assert.NotNull(ReflectionHelpers.GetField(be, "_savedNetworkState"));

    be.OnNetworkUpdate(State(vol: 0f, flow: 0f)); // empty + idle = not worth saving
    Assert.Null(ReflectionHelpers.GetField(be, "_savedNetworkState"));
  }

  [Fact]
  public void Display_fields_and_network_state_round_trip_through_the_tree() {
    var (_, be) = NewPipe();
    be.OnNetworkUpdate(
      State(vol: 450f, temp: 160f, medium: "Steam", pressure: 3f)
    );

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var (world2, restored) = NewPipe();
    restored.FromTreeAttributes(tree, world2.World);

    Assert.Equal(450f, restored.Volume, 3);
    Assert.Equal(160f, restored.Temperature, 3);
    Assert.Equal("Steam", restored.Medium);
    Assert.Equal(3f, restored.Pressure, 3);
    Assert.NotNull(ReflectionHelpers.GetField(restored, "_savedNetworkState"));
  }

  [Fact]
  public void Orientation_metadata_round_trips() {
    var (_, be) = NewPipe();
    be.Orientation = "we";
    be.PossibleOrientations = ["ns", "we"];

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var (world2, restored) = NewPipe();
    restored.FromTreeAttributes(tree, world2.World);

    Assert.Equal("we", restored.Orientation);
    Assert.Equal(new[] { "ns", "we" }, restored.PossibleOrientations);
  }

  [Fact]
  public void Empty_network_state_does_not_deserialize_a_pool() {
    // A pipe saved with a zero-volume pool must restore with no cached state, so it does not
    // re-inject a phantom pool on load.
    var (_, be) = NewPipe();
    be.OnNetworkUpdate(State(vol: 0f, flow: 0f));

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var (world2, restored) = NewPipe();
    restored.FromTreeAttributes(tree, world2.World);

    Assert.Null(ReflectionHelpers.GetField(restored, "_savedNetworkState"));
  }

  [Fact]
  public void A_live_network_broadcasts_pressure_into_placed_pipe_entities() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var pipe = PipeTestWorld.MakePipe();
    var bes = new BlockEntityPipe[3];
    for (int z = 0; z < 3; z++) {
      var pos = new BlockPos(0, 0, z);
      bes[z] = new BlockEntityPipe { Pos = pos, Block = pipe };
      world.Place(pos, pipe, bes[z]);
      world.Attach(bes[z]);
    }
    // Seal the ends so a gas can build pressure instead of leaking out.
    var rock = TestBlocks.Configure(
      new Vintagestory.API.Common.Block(),
      "game:rock",
      99
    );
    world.Place(new BlockPos(0, 0, -1), rock);
    world.Place(new BlockPos(0, 0, 3), rock);

    for (int z = 0; z < 3; z++)
      world.AddNode(new BlockPos(0, 0, z), "pipe");

    var net = (PipeNetwork)world.NetworkAt(new BlockPos(0, 0, 0))!;
    net.TryProduceGas(
      450f,
      150f,
      "Steam",
      world.Accessor,
      maxOutputPressure: 10f
    );
    net.BroadcastUpdate(world.Accessor);

    foreach (var be in bes) {
      Assert.Equal("Steam", be.Medium);
      Assert.True(
        be.Pressure > 0f,
        "each pipe should see the network pressure"
      );
    }
  }
}

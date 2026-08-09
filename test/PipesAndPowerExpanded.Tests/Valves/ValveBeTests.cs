using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The in-line gas valve block entity: an open valve is a normal pipe node (the run flows through as
/// one network); a closed valve severs the run at its own cell. Toggling re-walks the graph. A closed
/// valve must also drop any pool it cached while open, or the pressurised state would burst the
/// isolated cell on reload.
/// </summary>
public class ValveBeTests {
  /// <summary>A 3-cell run along +Z with a valve in the middle cell; ends sealed against rock so a
  /// gas can pressurise. Every cell carries the same ns pipe block.</summary>
  private static (TestWorld world, BlockEntityValve valve) Run() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pipe = PipeTestWorld.MakePipe();

    var valve = new BlockEntityValve {
      Pos = new BlockPos(0, 0, 1),
      Block = pipe,
    };
    world.Place(new BlockPos(0, 0, 0), pipe);
    world.Place(new BlockPos(0, 0, 1), pipe, valve);
    world.Place(new BlockPos(0, 0, 2), pipe);

    var rock = TestBlocks.Configure(new Block(), "game:rock", 99);
    world.Place(new BlockPos(0, 0, -1), rock);
    world.Place(new BlockPos(0, 0, 3), rock);

    world.Initialize(valve); // registers the valve node + captures the manager
    world.AddNode(new BlockPos(0, 0, 0), "pipe");
    world.AddNode(new BlockPos(0, 0, 2), "pipe");
    return (world, valve);
  }

  [Fact]
  public void Closed_by_default_severs_the_run() {
    var (world, _) = Run();
    Assert.NotSame(
      world.NetworkAt(new BlockPos(0, 0, 0)),
      world.NetworkAt(new BlockPos(0, 0, 2))
    );
  }

  [Fact]
  public void IsConnectionBroken_tracks_the_open_state() {
    var (_, valve) = Run();
    Assert.False(valve.IsOpen());
    Assert.True(valve.IsConnectionBroken());

    valve.ToggleOpen();
    Assert.True(valve.IsOpen());
    Assert.False(valve.IsConnectionBroken());
  }

  [Fact]
  public void Opening_rejoins_both_sides_into_one_network() {
    var (world, valve) = Run();
    valve.ToggleOpen(); // open

    Assert.Same(
      world.NetworkAt(new BlockPos(0, 0, 0)),
      world.NetworkAt(new BlockPos(0, 0, 2))
    );
  }

  [Fact]
  public void Closing_again_re_severs_the_run() {
    var (world, valve) = Run();
    valve.ToggleOpen(); // open -> joined
    valve.ToggleOpen(); // closed -> severed

    Assert.NotSame(
      world.NetworkAt(new BlockPos(0, 0, 0)),
      world.NetworkAt(new BlockPos(0, 0, 2))
    );
  }

  [Fact]
  public void Closing_discards_the_pool_cached_while_open() {
    var (world, valve) = Run();
    valve.ToggleOpen(); // open

    // Pressurise the joined run, then broadcast so the valve caches the (meaningful) pool.
    var net = (PipeNetwork)world.NetworkAt(valve.Pos)!;
    net.TryProduceGas(
      450f,
      150f,
      "Steam",
      world.Accessor,
      maxOutputPressure: 10f
    );
    net.BroadcastUpdate(world.Accessor);
    Assert.NotNull(ReflectionHelpers.GetField(valve, "_savedNetworkState"));

    valve.ToggleOpen(); // close -> must clear the stale pool

    Assert.Null(ReflectionHelpers.GetField(valve, "_savedNetworkState"));
    Assert.Equal(0f, valve.Pressure, 3);
  }

  [Fact]
  public void Open_state_round_trips_through_the_tree() {
    var (world, valve) = Run();
    valve.ToggleOpen(); // open

    var tree = new TreeAttribute();
    valve.ToTreeAttributes(tree);

    var restored = new BlockEntityValve {
      Pos = valve.Pos.Copy(),
      Block = valve.Block,
    };
    world.Attach(restored);
    restored.FromTreeAttributes(tree, world.World);

    Assert.True(restored.IsOpen());
  }

  [Fact]
  public void Loading_a_closed_valve_drops_any_persisted_pool() {
    // A hand-built save tree: valve closed but still carrying a pressurised pool (the exact
    // shape the burst bug produced). FromTree must discard it before Initialize can restore it.
    var world = new TestWorld();
    var pipe = PipeTestWorld.MakePipe();
    var valve = new BlockEntityValve {
      Pos = new BlockPos(0, 0, 0),
      Block = pipe,
    };
    world.Place(valve.Pos, pipe, valve);
    world.Attach(valve);

    var tree = new TreeAttribute();
    tree.SetString("networkType", "pipe");
    tree.SetString("orientation", "ns");
    tree.SetString("possibleOrientations", "[]");
    tree.SetBool("valveOpen", false);
    tree.SetFloat("vol", 450f); // a leftover pressurised pool
    tree.SetFloat("max", 90f);
    tree.SetFloat("pressure", 5f);

    valve.FromTreeAttributes(tree, world.World);

    Assert.False(valve.IsOpen());
    Assert.Null(ReflectionHelpers.GetField(valve, "_savedNetworkState"));
  }
}

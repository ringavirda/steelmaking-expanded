using System.Linq;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Named reproductions of specific bugs that were fixed, so they can never silently return. Each test
/// encodes the original failure as an assertion: if the fix is reverted, the test goes red.
/// </summary>
public class RegressionScenarioTests {
  /// <summary>
  /// Over-pressure (volume above the 1-atm capacity, up to the burst ceiling) must survive a graph
  /// re-walk. The bug: OnMerge/OnSplitFragment clamped gas at MaxVolume, so every split/merge (e.g. a
  /// valve toggle) dumped a pressurised run back to 1 atm.
  /// </summary>
  [Fact]
  public void Over_pressure_survives_a_node_remove_and_readd() {
    var (w, net) = PipeTestWorld.Run(5, "iron", capEnds: true);
    // Charge well above 1 atm (maxVolume = 5 * 30 = 150 L; ~3 atm).
    net.TryProduceGas(450f, 150f, "Steam", w.Accessor, maxOutputPressure: 5f);
    Assert.True(net.State!.Pressure > 2.5f);

    // Re-walk the graph: remove a middle cell (splits) then re-add it (merges) with no tick in
    // between, so nothing leaks - isolating the merge/split clamp.
    var mid = new BlockPos(0, 0, 2);
    w.RemoveNode(mid);
    w.AddNode(mid, "pipe");

    var rejoined = w.NetworkAt(new BlockPos(0, 0, 0))!;
    Assert.True(
      ((PipeNetworkState)rejoined.State!).Pressure > 1.5f,
      "the re-walked run was dumped back toward 1 atm (over-pressure clamp regression)"
    );
  }

  /// <summary>
  /// A closed valve must not restore a pressurised pool on reload. The bug: a valve cached its pool
  /// while open, then persisted+restored it into the now-isolated cell, bursting it.
  /// </summary>
  [Fact]
  public void Closed_valve_does_not_reload_a_pressurised_pool() {
    var (world, valve) = ValveScenario();
    valve.ToggleOpen(); // open

    var net = (PipeNetwork)world.NetworkAt(valve.Pos)!;
    net.TryProduceGas(
      450f,
      150f,
      "Steam",
      world.Accessor,
      maxOutputPressure: 10f
    );
    net.BroadcastUpdate(world.Accessor);

    valve.ToggleOpen(); // close - must drop the cached pool

    Assert.Null(ReflectionHelpers.GetField(valve, "_savedNetworkState"));
    Assert.Equal(0f, valve.Pressure, 3);
  }

  /// <summary>
  /// A constructed machine's production tick must not stack-overflow. The bug:
  /// <c>BlockEntityProductionMachine.NetworkAt</c>/<c>ConnectedNetwork</c> delegated with
  /// <c>this.NetworkAt(...)</c>, which bound to the instance method itself (instance methods shadow
  /// extensions) and recursed forever on every machine tick.
  /// </summary>
  [Fact]
  public void Constructed_engine_tick_does_not_recurse() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var eng = new EngineFixture(scene, new BlockPos(0, 8, 0));
    scene.Build();
    eng.SetInletPressure(3f);

    scene.Step(); // would stack-overflow if the network-port recursion returned

    Assert.True(eng.Engine.AvailablePower > 0f);
  }

  /// <summary>
  /// A run that has been fully drained but not yet cleared (the 3-second empty-clear delay keeps its
  /// State alive so a busy push-and-drain line doesn't flicker) still carries its old medium LABEL.
  /// The bug class (same shape as the cowper mix-latch): a producer of the OTHER medium read that
  /// stale label and was rejected, so repurposing empty pipes was blocked for up to 3 seconds. A
  /// physically empty run (Volume 0) must let a new medium re-claim it.
  /// </summary>
  [Fact]
  public void A_drained_run_accepts_the_other_medium_before_the_label_clears() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3); // MaxVolume 90

    // Fill with water, then drain it fully WITHOUT ticking past the clear delay: the run is
    // physically empty (Volume 0) but still labelled "Water".
    net.TryProduceLiquid(60f, 20f, 1f, w.Accessor);
    Assert.Equal(60f, net.TryConsumeLiquid(999f, w.Accessor), 3);
    Assert.Equal(0f, net.State!.Volume, 3);
    Assert.Equal("Water", net.State.MediumType); // stale display label survives

    // Steam reusing the empty pipes must not be latched out by the leftover "Water" label.
    bool ok = net.TryProduceGas(45f, 150f, "Steam", w.Accessor);

    Assert.True(ok, "an empty run must let a new medium re-claim it");
    Assert.Equal("Steam", net.State!.MediumType);
    Assert.Equal(45f, net.State.Volume, 3);
  }

  /// <summary>The mirror of the above: a drained-but-labelled GAS run accepts water.</summary>
  [Fact]
  public void A_drained_gas_run_accepts_water_before_the_label_clears() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);

    net.TryProduceGas(45f, 150f, "Steam", w.Accessor);
    Assert.Equal(45f, net.TryConsumeGas(999f, w.Accessor), 3);
    Assert.Equal(0f, net.State!.Volume, 3);
    Assert.Equal("Steam", net.State.MediumType);

    bool ok = net.TryProduceLiquid(30f, 20f, 1f, w.Accessor);

    Assert.True(ok, "an empty run must let water re-claim it");
    Assert.Equal("Water", net.State!.MediumType);
    Assert.Equal(30f, net.State.Volume, 3);
  }

  /// <summary>A run still physically carrying a medium must STILL reject the other one - the fix
  /// only relaxes the guard for an empty run, not a full one (guards against over-fixing).</summary>
  [Fact]
  public void A_run_still_holding_a_medium_rejects_the_other() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);
    net.TryProduceLiquid(30f, 20f, 1f, w.Accessor); // Volume 30, "Water"

    Assert.False(net.TryProduceGas(10f, 120f, "Air", w.Accessor));
    Assert.Equal("Water", net.State!.MediumType);
  }

  private static (
    TestWorld world,
    PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities.BlockEntityValve valve
  ) ValveScenario() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", s => new PipeNetwork(s));
    var pipe = PipeTestWorld.MakePipe();
    var valve =
      new PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities.BlockEntityValve {
        Pos = new BlockPos(0, 0, 1),
        Block = pipe,
      };
    world.Place(new BlockPos(0, 0, 0), pipe);
    world.Place(new BlockPos(0, 0, 1), pipe, valve);
    world.Place(new BlockPos(0, 0, 2), pipe);
    var rock = TestBlocks.Configure(
      new Vintagestory.API.Common.Block(),
      "game:rock",
      99
    );
    world.Place(new BlockPos(0, 0, -1), rock);
    world.Place(new BlockPos(0, 0, 3), rock);
    world.Initialize(valve);
    world.AddNode(new BlockPos(0, 0, 0), "pipe");
    world.AddNode(new BlockPos(0, 0, 2), "pipe");
    return (world, valve);
  }
}

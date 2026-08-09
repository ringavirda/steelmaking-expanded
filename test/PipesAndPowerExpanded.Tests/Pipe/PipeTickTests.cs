using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>Time-driven behaviour: throughput/pressure refresh, idle clearing, evaporation,
/// open-end leak loss, and over-pressure burst - all via <see cref="TestWorld.Tick"/>.</summary>
public class PipeTickTests {
  [Fact]
  public void Tick_refreshes_pressure_and_flow_rate() {
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true);
    net.TryProduceGas(45f, 120f, "Steam", w.Accessor);

    w.Tick();

    Assert.True(
      net.State!.FlowRate > 0f,
      "throughput should register the production"
    );
    Assert.Equal(0.5f, net.State.Pressure, 2);
  }

  [Fact]
  public void Drained_run_clears_only_after_idle_delay() {
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true);
    net.TryProduceGas(45f, 120f, "Air", w.Accessor);
    net.TryConsumeGas(45f, w.Accessor); // volume now 0, but just had flow

    w.Tick(6); // > the 3 s empty-clear delay with no further flow

    Assert.Null(net.State);
  }

  [Fact]
  public void Water_run_evaporates_with_the_calendar() {
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true);
    net.TryProduceLiquid(1000f, 20f, 1f, w.Accessor); // full = 90 L

    w.Tick(); // stamps the evaporation clock, charges nothing yet
    w.AdvanceDays(1);
    w.Tick();

    Assert.Equal(40f, net.State!.Volume, 1); // 90 - 50 L/day
  }

  [Fact]
  public void Open_ended_run_leaks_gas_each_tick() {
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: false); // open air ends
    net.TryProduceGas(450f, 200f, "Steam", w.Accessor, maxOutputPressure: 10f);

    w.Tick();

    Assert.True(
      net.State!.OpeningsCount > 0,
      "open ends should be detected as leaks"
    );
    Assert.True(net.State.Volume < 450f, "leaking gas should bleed volume");
  }

  [Fact]
  public void Sealed_overpressured_run_bursts_after_the_grace_period() {
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true);
    net.TryProduceGas(1000f, 200f, "Steam", w.Accessor, maxOutputPressure: 10f);
    Assert.Equal(450f, net.State!.Volume, 3); // sitting at the 5-atm burst pressure

    w.Tick(30); // PipeOverpressureSeconds

    Assert.NotEmpty(w.Drops); // a burst pipe drops its materials
    int remaining = w.Networks.AllNetworks.Sum(n => n.Nodes.Count);
    Assert.True(remaining < 3, "a pipe should have burst out of the run");
  }
}

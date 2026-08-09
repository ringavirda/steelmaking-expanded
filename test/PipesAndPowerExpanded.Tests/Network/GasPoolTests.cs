using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>Gas production/consumption, overflow ceilings, and the leak clamp.</summary>
public class GasPoolTests {
  [Fact]
  public void Produce_sets_volume_pressure_and_medium() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3); // MaxVolume 90

    bool ok = net.TryProduceGas(45f, 120f, "Steam", w.Accessor);

    Assert.True(ok);
    Assert.NotNull(net.State);
    Assert.Equal(45f, net.State!.Volume, 3);
    Assert.Equal(0.5f, net.State.Pressure, 3);
    Assert.Equal("Steam", net.State.MediumType);
  }

  [Fact]
  public void Produce_overflows_up_to_max_output_pressure() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3); // MaxVolume 90, no burst ceiling

    net.TryProduceGas(1000f, 120f, "Air", w.Accessor, maxOutputPressure: 2f);

    Assert.Equal(180f, net.State!.Volume, 3); // 2 atm * 90
    Assert.Equal(2f, net.State.Pressure, 3);
  }

  [Fact]
  public void Produce_is_capped_by_weakest_pipe_burst_pressure() {
    // Real iron pipes (burst 5 atm) cap the run below the producer's 10-atm choke.
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true); // MaxVolume 90

    net.TryProduceGas(1000f, 200f, "Steam", w.Accessor, maxOutputPressure: 10f);

    Assert.Equal(450f, net.State!.Volume, 3); // 5 atm * 90
    Assert.Equal(5f, net.State.Pressure, 3);
  }

  [Fact]
  public void Leaking_run_clamps_to_one_atm_unless_bypassed() {
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true);
    net.TryProduceGas(10f, 120f, "Air", w.Accessor); // create the pool
    net.State!.OpeningsCount = 1; // mark as leaking

    net.TryProduceGas(1000f, 120f, "Air", w.Accessor, maxOutputPressure: 10f);
    Assert.Equal(90f, net.State.Volume, 3); // clamped to 1 atm

    net.TryProduceGas(
      1000f,
      120f,
      "Air",
      w.Accessor,
      maxOutputPressure: 10f,
      bypassLeakCap: true
    );
    Assert.Equal(450f, net.State.Volume, 3); // bypass lifts the clamp to the 5-atm burst ceiling
  }

  [Fact]
  public void Consume_returns_min_of_request_and_available() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);
    net.TryProduceGas(45f, 120f, "Air", w.Accessor);

    Assert.Equal(45f, net.TryConsumeGas(100f, w.Accessor), 3);
    Assert.Equal(0f, net.State!.Volume, 3);
    Assert.Equal(0f, net.TryConsumeGas(100f, w.Accessor), 3);
  }

  [Fact]
  public void Water_run_rejects_gas_production() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);
    net.TryProduceLiquid(30f, 20f, 1f, w.Accessor);

    bool ok = net.TryProduceGas(10f, 120f, "Air", w.Accessor);

    Assert.False(ok);
    Assert.Equal("Water", net.State!.MediumType);
  }
}

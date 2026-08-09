using ExpandedLib.Testing;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>Water production/consumption and how feed pressure relates to fill level.</summary>
public class LiquidPoolTests {
  [Fact]
  public void Produce_caps_at_max_volume_and_realises_feed_pressure() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3); // MaxVolume 90

    net.TryProduceLiquid(1000f, 20f, setPressure: 3f, w.Accessor);

    Assert.Equal(90f, net.State!.Volume, 3); // a liquid cannot pack past capacity
    Assert.Equal(3f, net.State.Pressure, 3); // brim-full -> pump feed pressure
    Assert.Equal("Water", net.State.MediumType);
  }

  [Fact]
  public void Pressure_tracks_fill_ratio_below_brim_full() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);

    net.TryProduceLiquid(45f, 20f, setPressure: 3f, w.Accessor);

    Assert.Equal(45f, net.State!.Volume, 3);
    Assert.Equal(0.5f, net.State.Pressure, 3); // not yet at feed pressure
  }

  [Fact]
  public void Consume_drops_pressure_back_to_fill_ratio() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);
    net.TryProduceLiquid(1000f, 20f, setPressure: 3f, w.Accessor); // full, 3 atm

    float taken = net.TryConsumeLiquid(45f, w.Accessor);

    Assert.Equal(45f, taken, 3);
    Assert.Equal(45f, net.State!.Volume, 3);
    Assert.Equal(0.5f, net.State.Pressure, 3); // no longer brim-full
  }

  [Fact]
  public void Gas_run_rejects_water_production() {
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, 3);
    net.TryProduceGas(30f, 120f, "Air", w.Accessor);

    bool ok = net.TryProduceLiquid(10f, 20f, 1f, w.Accessor);

    Assert.False(ok);
    Assert.Equal("Air", net.State!.MediumType);
  }
}

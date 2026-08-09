using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Merge and fracture state handling - the source of several historical bugs (a valve toggle
/// re-walking the graph and dumping an over-pressured run back to 1 atm).
/// </summary>
public class MergeSplitTests {
  [Fact]
  public void Merge_same_medium_blends_temperature_and_sums_volume() {
    var w = new TestWorld();
    var a = PipeTestWorld.LooseNet(w.Networks, 2); // MaxVolume 60
    a.TryProduceGas(60f, 100f, "Air", w.Accessor);
    var b = PipeTestWorld.LooseNet(w.Networks, 2, baseZ: 100);
    b.TryProduceGas(60f, 200f, "Air", w.Accessor);

    // The manager folds b's nodes into a before calling OnMerge; mirror that.
    a.Nodes.Add(new BlockPos(0, 0, 100));
    a.Nodes.Add(new BlockPos(0, 0, 101));
    a.OnMerge(b, w.Accessor);

    Assert.Equal(120f, a.State!.Volume, 3);
    Assert.Equal(150f, a.State.Temperature, 3); // volume-weighted blend
  }

  [Fact]
  public void Merge_incompatible_media_keeps_the_larger_run() {
    var w = new TestWorld();
    var gas = PipeTestWorld.LooseNet(w.Networks, 2);
    gas.TryProduceGas(60f, 120f, "Air", w.Accessor);
    var water = PipeTestWorld.LooseNet(w.Networks, 3, baseZ: 100);
    water.TryProduceLiquid(90f, 20f, 1f, w.Accessor);

    for (int z = 100; z < 103; z++)
      gas.Nodes.Add(new BlockPos(0, 0, z));
    gas.OnMerge(water, w.Accessor);

    Assert.Equal("Water", gas.State!.MediumType); // 90 L water beat 60 L gas
    Assert.Equal(90f, gas.State.Volume, 3);
  }

  [Fact]
  public void SplitFragment_preserves_gas_over_pressure() {
    // Regression: a fracture must cap a gas fragment at the burst ceiling, not 1 atm.
    var (w, net) = PipeTestWorld.Run(3, "iron", capEnds: true);
    net.TryProduceGas(1000f, 200f, "Steam", w.Accessor, maxOutputPressure: 10f);
    Assert.Equal(450f, net.State!.Volume, 3); // 5 atm

    var fragment = new PipeNetwork(w.Networks);
    for (int z = 0; z < 3; z++)
      fragment.Nodes.Add(new BlockPos(0, 0, z)); // real iron pipes -> burst 5
    fragment.OnSplitFragment(net, w.Accessor);

    Assert.Equal(450f, fragment.State!.Volume, 3); // over-pressure kept, not dumped to 90
    Assert.Equal(5f, fragment.State.Pressure, 3);
  }

  [Fact]
  public void SplitFragment_takes_proportional_share() {
    var w = new TestWorld();
    var orig = PipeTestWorld.LooseNet(w.Networks, 4); // MaxVolume 120
    orig.TryProduceGas(80f, 120f, "Air", w.Accessor);

    var fragment = PipeTestWorld.LooseNet(w.Networks, 2, baseZ: 50);
    fragment.OnSplitFragment(orig, w.Accessor);

    Assert.Equal(40f, fragment.State!.Volume, 3); // 80 * 2/4
    Assert.Equal("Air", fragment.State.MediumType);
  }
}

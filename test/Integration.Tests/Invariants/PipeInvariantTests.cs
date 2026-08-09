using System;
using System.Linq;
using ExpandedLib.Testing;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Property/invariant tests: instead of one hand-picked scenario, hammer the pipe simulation with
/// many randomized sequences of produce/consume/tick and assert the laws that must hold for ALL of
/// them - finite, non-negative state; pressure never past the burst ceiling without bursting; no
/// gas created from nothing. These catch whole classes of bugs (NaNs, runaway pressure, sign slips)
/// that example tests miss.
/// </summary>
public class PipeInvariantTests {
  // The weakest (iron) pipe bursts at 5 atm; over-pressure may sit AT the ceiling but never above.
  private const float IronBurst = 5.0f;

  private static bool Finite(float f) =>
    !float.IsNaN(f) && !float.IsInfinity(f);

  [Theory]
  [InlineData(1)]
  [InlineData(7)]
  [InlineData(13)]
  [InlineData(99)]
  [InlineData(1234)]
  public void Sealed_gas_run_never_goes_NaN_or_past_the_burst_ceiling(int seed) {
    var rng = new Random(seed);
    int length = rng.Next(1, 9);
    var (w, net) = PipeTestWorld.Run(length, "iron", capEnds: true);

    for (int op = 0; op < 40; op++) {
      switch (rng.Next(3)) {
        case 0:
          net.TryProduceGas(
            (float)rng.NextDouble() * 400f,
            20f + (float)rng.NextDouble() * 300f,
            "Steam",
            w.Accessor,
            maxOutputPressure: 1f + (float)rng.NextDouble() * 12f
          );
          break;
        case 1:
          net.TryConsumeGas((float)rng.NextDouble() * 400f, w.Accessor);
          break;
        default:
          w.Tick(rng.Next(1, 4));
          break;
      }

      var s = net.State;
      if (s == null)
        continue;

      Assert.True(
        Finite(s.Volume) && Finite(s.Pressure) && Finite(s.Temperature),
        $"state went non-finite (seed {seed}, op {op})"
      );
      Assert.True(
        s.Volume >= -0.001f,
        $"negative volume {s.Volume} (seed {seed})"
      );
      // Over-pressure is allowed up to the burst ceiling; beyond it the run must have burst
      // (fewer nodes) rather than hold an impossible pressure.
      bool burst = w.Networks.AllNetworks.Sum(n => n.Nodes.Count) < length;
      Assert.True(
        s.Pressure <= IronBurst + 0.01f || burst,
        $"pressure {s.Pressure} exceeded burst ceiling without bursting (seed {seed})"
      );
    }
  }

  // Order-randomizing invariant (the cowper lesson generalized to a property): randomly interleave
  // gas/water production, consumption, and ticks - including MEDIUM SWITCHES on a drained run - and
  // assert the law that example tests kept missing: an EMPTY run (State null, or Volume ~0) must
  // never permanently reject a fresh medium. A latched stale-label guard would eventually wedge a
  // physically empty run so neither medium could ever re-claim it.
  [Theory]
  [InlineData(3)]
  [InlineData(64)]
  [InlineData(512)]
  [InlineData(4096)]
  public void An_empty_run_can_always_be_re_claimed_by_either_medium(int seed) {
    var rng = new Random(seed);
    var w = new TestWorld();
    var net = PipeTestWorld.LooseNet(w.Networks, rng.Next(1, 6));

    for (int op = 0; op < 60; op++) {
      switch (rng.Next(4)) {
        case 0:
          net.TryProduceGas(
            (float)rng.NextDouble() * 200f,
            20f + (float)rng.NextDouble() * 200f,
            rng.Next(2) == 0 ? "Steam" : "Air",
            w.Accessor,
            maxOutputPressure: 1f + (float)rng.NextDouble() * 3f
          );
          break;
        case 1:
          net.TryProduceLiquid(
            (float)rng.NextDouble() * 200f,
            20f,
            1f,
            w.Accessor
          );
          break;
        case 2:
          net.TryConsumeGas((float)rng.NextDouble() * 300f, w.Accessor);
          net.TryConsumeLiquid((float)rng.NextDouble() * 300f, w.Accessor);
          break;
        default:
          w.Tick(rng.Next(1, 3));
          break;
      }

      var s = net.State;
      Assert.True(
        s == null || (Finite(s.Volume) && s.Volume >= -0.001f),
        $"state went bad (seed {seed}, op {op})"
      );

      // The invariant: drain the run completely, and BOTH mediums must be accepted into it (one of
      // them re-claims the empty pipes). A stale-label latch would fail this.
      net.TryConsumeGas(float.MaxValue, w.Accessor);
      net.TryConsumeLiquid(float.MaxValue, w.Accessor);
      if ((net.State?.Volume ?? 0f) <= 0.001f) {
        bool gas = net.TryProduceGas(10f, 100f, "Air", w.Accessor);
        bool water = !gas && net.TryProduceLiquid(10f, 20f, 1f, w.Accessor);
        Assert.True(
          gas || water,
          $"an empty run rejected BOTH mediums - latched out (seed {seed}, op {op})"
        );
        // Reset to empty for the next iteration so leftover from this probe doesn't skew it.
        net.TryConsumeGas(float.MaxValue, w.Accessor);
        net.TryConsumeLiquid(float.MaxValue, w.Accessor);
      }
    }
  }

  [Theory]
  [InlineData(2)]
  [InlineData(42)]
  [InlineData(777)]
  public void Consume_never_returns_more_than_was_present(int seed) {
    var rng = new Random(seed);
    var (w, net) = PipeTestWorld.Run(rng.Next(1, 6), "iron", capEnds: true);
    net.TryProduceGas(200f, 150f, "Steam", w.Accessor, maxOutputPressure: 10f);

    float beforeVol = net.State?.Volume ?? 0f;
    float drawn = net.TryConsumeGas(1_000_000f, w.Accessor); // ask for far more than exists

    Assert.True(
      drawn <= beforeVol + 0.001f,
      $"drew {drawn} from {beforeVol} (seed {seed})"
    );
    Assert.True(drawn >= 0f);
    Assert.True((net.State?.Volume ?? 0f) >= -0.001f);
  }
}

using ExpandedLib.Blocks.Machines;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>The shared hysteresis accumulator used by boiler/engine failure timers.</summary>
public class GraceTimerTests {
  [Fact]
  public void Fires_once_after_threshold_then_rearms() {
    var timer = new GraceTimer();

    Assert.False(timer.Update(true, 1f, 3f)); // 1s
    Assert.False(timer.Update(true, 1f, 3f)); // 2s
    Assert.True(timer.Update(true, 1f, 3f)); // 3s -> fire + reset
    Assert.Equal(0f, timer.Elapsed, 3);
    Assert.False(timer.Update(true, 1f, 3f)); // re-armed, counting again
  }

  [Fact]
  public void Inactive_tick_resets_the_accumulator() {
    var timer = new GraceTimer();
    timer.Update(true, 2f, 3f);
    Assert.True(timer.IsCounting);

    Assert.False(timer.Update(false, 1f, 3f)); // condition lifted
    Assert.Equal(0f, timer.Elapsed, 3);
    Assert.False(timer.IsCounting);
  }

  [Fact]
  public void Remaining_counts_down_toward_the_threshold() {
    var timer = new GraceTimer();
    timer.Update(true, 2f, 5f);
    Assert.Equal(3f, timer.Remaining(5f), 3);
  }

  [Fact]
  public void Reset_clears_without_firing() {
    var timer = new GraceTimer();
    timer.Update(true, 2f, 5f);
    timer.Reset();
    Assert.Equal(0f, timer.Elapsed, 3);
  }

  [Fact]
  public void Round_trips_through_a_tree() {
    var timer = new GraceTimer();
    timer.Update(true, 2.5f, 10f);
    var tree = new TreeAttribute();
    timer.ToTree(tree, "grace");

    var restored = new GraceTimer();
    restored.FromTree(tree, "grace");
    Assert.Equal(2.5f, restored.Elapsed, 3);
  }
}

using PipesAndPowerExpanded.BlockStructures.Engine;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The frame-crossing test behind every engine stroke sound. It runs on the 50 ms client tick, so a
/// predicate that answers "yes" when the cycle did not actually sweep the keyframe turns two sounds
/// per revolution into roughly twenty per second per engine - enough for a handful of machines to
/// exhaust the game's 250 concurrent-sound cap and silence all audio, the mod's and vanilla's alike.
/// </summary>
public class PistonCycleSoundsTests {
  private const int Total = 60; // the 60-frame cylinder loop the engines animate on
  #region Forward motion

  [Fact]
  public void A_forward_step_fires_the_keyframe_it_swept() {
    Assert.True(PistonCycleSounds.CrossedFrame(10f, 12.4f, Total, 12));
  }

  [Fact]
  public void A_forward_step_does_not_fire_a_keyframe_it_has_not_reached() {
    Assert.False(PistonCycleSounds.CrossedFrame(10f, 12.4f, Total, 15));
  }

  [Fact]
  public void A_forward_step_does_not_refire_a_keyframe_already_behind_it() {
    Assert.False(PistonCycleSounds.CrossedFrame(20f, 22.4f, Total, 15));
  }

  [Fact]
  public void A_wrap_past_the_loop_end_still_fires_the_keyframes_it_swept() {
    // 59 -> 1.4 sweeps across 0.
    Assert.True(PistonCycleSounds.CrossedFrame(59f, 1.4f, Total, 0));
    Assert.True(PistonCycleSounds.CrossedFrame(59f, 1.4f, Total, 1));
    Assert.False(PistonCycleSounds.CrossedFrame(59f, 1.4f, Total, 45));
  }

  #endregion

  #region Backward motion

  // The engine deliberately supports a cycle running backwards (a reversed mechanical network, and
  // the sub-machine phase-lock writes CurrentFrame directly and can jump back). A backward step
  // arrives here as cur < last, which used to be read as "wrapped past the end" - so the predicate
  // answered true for every keyframe outside the tiny interval actually stepped over, on almost
  // every tick. That is the sound spam.

  [Fact]
  public void A_backward_step_is_not_mistaken_for_a_wrap() {
    Assert.False(PistonCycleSounds.CrossedFrame(45f, 42.6f, Total, 15));
    Assert.False(PistonCycleSounds.CrossedFrame(45f, 42.6f, Total, 45));
  }

  [Fact]
  public void A_backward_run_stays_silent_across_a_whole_revolution() {
    // Walk the loop backwards a tick at a time; nothing may fire.
    float last = 59f;
    for (int i = 0; i < Total * 2; i++) {
      float cur = last - 2.4f;
      if (cur < 0)
        cur += Total;
      Assert.False(
        PistonCycleSounds.CrossedFrame(last, cur, Total, 15),
        $"fired at last={last}, cur={cur}"
      );
      Assert.False(
        PistonCycleSounds.CrossedFrame(last, cur, Total, 45),
        $"fired at last={last}, cur={cur}"
      );
      last = cur;
    }
  }

  #endregion

  #region Degenerate input

  [Fact]
  public void A_stationary_cycle_fires_nothing() {
    Assert.False(PistonCycleSounds.CrossedFrame(30f, 30f, Total, 30));
  }

  [Fact]
  public void A_single_frame_animation_fires_nothing() {
    Assert.False(PistonCycleSounds.CrossedFrame(0f, 0f, 1, 0));
  }

  #endregion
}

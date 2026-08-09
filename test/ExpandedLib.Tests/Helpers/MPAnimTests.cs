using ExpandedLib.Helpers;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The phase-lock math that keeps a driven part turning WITH a mechanical-power axle rather than
/// merely at the same rate. Three machines now hang off it - the engine's MP cycle, the twin-tub
/// blower and the mechanical pump - and it went untested while it had no callers at all.
/// </summary>
public class MPAnimTests {
  private const int Frames = 60;

  #region FrameFromAngle

  [Fact]
  public void A_full_turn_maps_onto_the_whole_frame_space() {
    // The span is the FULL frame count, not the last keyframe's number. The animator's live frame
    // space is [0, QuantityFrames): the stretch from the last keyframe back to the first is an
    // ordinary interpolation segment that it renders like any other, one frame wide. Spanning
    // total-1 maps a revolution onto everything BUT that segment, so the segment is reached only by
    // the clip's own advance - out of step with the axle, and the pop the players saw.
    Assert.Equal(0f, MPAnim.FrameFromAngle(0f, Frames), 4);
    Assert.Equal(
      Frames / 4f,
      MPAnim.FrameFromAngle(GameMath.PIHALF, Frames),
      4
    );
    Assert.Equal(Frames / 2f, MPAnim.FrameFromAngle(GameMath.PI, Frames), 4);
  }

  [Fact]
  public void The_last_frame_is_reachable_so_the_wrap_segment_is_never_skipped() {
    // The bug in one assertion: with a span of total-1 the frames between the last keyframe and the
    // count were unreachable, and the animator's own advance walked into them anyway.
    float justShy = MPAnim.FrameFromAngle(GameMath.TWOPI - 0.001f, Frames);

    Assert.InRange(justShy, Frames - 1f, Frames);
  }

  [Fact]
  public void A_whole_revolution_returns_to_the_first_frame() {
    Assert.Equal(
      MPAnim.FrameFromAngle(0f, Frames),
      MPAnim.FrameFromAngle(GameMath.TWOPI, Frames),
      4
    );
    Assert.Equal(
      MPAnim.FrameFromAngle(0.4f, Frames),
      MPAnim.FrameFromAngle(0.4f + GameMath.TWOPI, Frames),
      4
    );
  }

  [Fact]
  public void The_frame_never_leaves_the_frame_space() {
    for (int i = -720; i <= 720; i += 7) {
      float frame = MPAnim.FrameFromAngle(i * GameMath.DEG2RAD, Frames);
      Assert.InRange(frame, 0f, Frames);
    }
  }

  [Fact]
  public void A_reversed_axle_runs_the_cycle_backwards() {
    // Direction comes free from the angle itself - the caller passes the axle's render angle, which
    // decreases when the shaft turns the other way. This is why the absolute mapping replaced the
    // accumulating one: no baseline to reset, and no drift over a long run.
    float forward = MPAnim.FrameFromAngle(GameMath.PIHALF, Frames);
    float backward = MPAnim.FrameFromAngle(-GameMath.PIHALF, Frames);

    Assert.Equal(Frames - forward, backward, 3);
  }

  [Fact]
  public void A_degenerate_frame_count_is_pinned_to_zero_rather_than_dividing_by_zero() {
    Assert.Equal(0f, MPAnim.FrameFromAngle(1.2f, 1), 4);
    Assert.Equal(0f, MPAnim.FrameFromAngle(1.2f, 0), 4);
  }

  [Fact]
  public void Reversing_mirrors_the_frame_about_the_cycle() {
    // The flag exists because a clip authored turning its shaft through +360 runs against the
    // vanilla axle for a rising angle, which reads as the machine's shaft spinning backwards next
    // to the axle driving it.
    foreach (float angle in new[] { 0.3f, 1.7f, 3.9f, 5.5f }) {
      float forward = MPAnim.FrameFromAngle(angle, Frames);
      float reversed = MPAnim.FrameFromAngle(-angle, Frames);
      Assert.Equal(Frames - forward, reversed, 3);
    }
  }

  #endregion

  #region AdvanceFrame

  [Fact]
  public void Advancing_a_full_turn_advances_exactly_one_cycle() {
    // The relative form spans the same full frame count as FrameFromAngle; it differs only in
    // taking a step rather than an absolute angle, for parts that care about rate and direction.
    float frame = MPAnim.AdvanceFrame(0f, 0f, GameMath.PI, Frames);
    Assert.Equal(Frames / 2f, frame, 3);
  }

  [Fact]
  public void Advancing_wraps_instead_of_growing_without_bound() {
    float frame = 0f;
    for (int i = 0; i < 12; i++)
      frame = MPAnim.AdvanceFrame(frame, 0f, GameMath.PIHALF, Frames);

    Assert.InRange(frame, 0f, Frames);
  }

  [Fact]
  public void A_backward_step_moves_the_frame_backward() {
    float frame = MPAnim.AdvanceFrame(30f, 0f, -GameMath.PIHALF, Frames);
    Assert.Equal(30f - Frames / 4f, frame, 3);
  }

  [Fact]
  public void A_degenerate_frame_count_is_pinned_to_zero() {
    Assert.Equal(0f, MPAnim.AdvanceFrame(5f, 0f, 1f, 1), 4);
  }

  #endregion
}

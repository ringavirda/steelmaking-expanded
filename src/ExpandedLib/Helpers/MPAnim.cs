using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>
/// Phase-lock math for mega-block parts (a rotor, a gear, a piston) that must turn in step with a
/// mechanical-power axle rather than merely at a proportional speed. A driven part reads the
/// network's rotation angle each render frame and derives its cyclic animation frame from it, so one
/// full network revolution plays exactly one animation cycle and the part does not drift out of
/// phase with the axle.
/// </summary>
public static class MPAnim {
  /// <summary>
  /// Returns the next cyclic frame for an animation of <paramref name="totalFrames"/> frames, given
  /// the part's <paramref name="currentFrame"/> and the network angle on the previous frame
  /// (<paramref name="lastAngleRad"/>) and now (<paramref name="angleRad"/>). The result wraps within
  /// <c>[0, totalFrames)</c>; one full revolution (2π) advances exactly one cycle. Returns 0 when
  /// <paramref name="totalFrames"/> is 1 or less.
  /// </summary>
  public static float AdvanceFrame(
    float currentFrame,
    float lastAngleRad,
    float angleRad,
    int totalFrames
  ) {
    if (totalFrames <= 1)
      return 0f;
    // AngleRadDistance gives the signed shortest delta, so wrapping past 2π or reversing advances
    // the frame without a jump.
    float delta = GameMath.AngleRadDistance(lastAngleRad, angleRad);
    return GameMath.Mod(
      currentFrame + delta / GameMath.TWOPI * totalFrames,
      totalFrames
    );
  }

  /// <summary>
  /// Maps a network rotation angle straight onto a cyclic animation frame, so a driven part stays
  /// locked to the axle's absolute angle rather than merely spinning at the same rate. Angle
  /// <c>0..2π</c> maps onto frame <c>0..(totalFrames-1)</c> and wraps at <c>2π</c>; a full-turn
  /// animation's first and last keyframes share an orientation, so the wrap is invisible. Use
  /// <see cref="AdvanceFrame"/> instead when only speed and direction matter, such as an oscillating
  /// piston that need not align to an absolute angle.
  /// </summary>
  public static float FrameFromAngle(float angleRad, int totalFrames) {
    if (totalFrames <= 1)
      return 0f;
    // Span over the keyframe range [0, total-1]: the last keyframe ends the turn and the wrap
    // (total-1 -> 0) is the same orientation, so it never interpolates backward through the loop.
    int span = totalFrames - 1;
    return GameMath.Mod(angleRad / GameMath.TWOPI * span, span);
  }
}

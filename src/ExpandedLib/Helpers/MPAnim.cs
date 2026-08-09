using Vintagestory.API.Common;
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
  /// <c>0..2π</c> maps onto frame <c>0..totalFrames</c> and wraps at <c>2π</c>. Use
  /// <see cref="AdvanceFrame"/> instead when only speed and direction matter, such as an oscillating
  /// piston that need not align to an absolute angle.
  /// </summary>
  /// <remarks>
  /// The span is the FULL frame count, not the last keyframe's number. The animator's live frame
  /// space is <c>[0, QuantityFrames)</c>, and the stretch from the last keyframe back to the first
  /// is an ordinary interpolation segment that gets rendered like any other - one frame wide, and
  /// carrying the last <c>360/QuantityFrames</c> degrees of the turn. Spanning <c>total-1</c> maps a
  /// whole revolution onto everything but that segment, which both compresses the cycle and leaves
  /// the segment to be reached only by the clip's own advance, out of step with the axle. So a clip
  /// driven from here is authored the way vanilla authors one: last keyframe at
  /// <c>360 * (frames-1) / frames</c> with <c>rotShortestDistance</c> set, never a duplicate of
  /// frame 0.
  /// </remarks>
  public static float FrameFromAngle(float angleRad, int totalFrames) {
    if (totalFrames <= 1)
      return 0f;
    return GameMath.Mod(angleRad / GameMath.TWOPI * totalFrames, totalFrames);
  }

  /// <summary>
  /// Pins the running <paramref name="animCode"/> clip's frame to <paramref name="angleRad"/>, so
  /// the part it drives turns with the axle instead of merely at the same rate. Call once per render
  /// frame; a client tick is far too coarse and shows as stepping. A no-op when the clip is not
  /// running or the animator has not resolved, so it is safe to call unconditionally.
  /// <para>
  /// Two things have to be true for the write to be visible, and neither is obvious. The clip must
  /// have been started with a NON-ZERO <c>AnimationSpeed</c> - the animator does not pose a
  /// zero-speed animation at all, so every frame written here would be discarded. And it must loop
  /// (<c>onAnimationEnd: Repeat</c>), or the animator drops it at the end and an animator-rendered
  /// block loses its mesh. The clip's own advance cannot be switched off and lands AFTER this write
  /// - the animator ticks at <c>EnumRenderStage.Opaque</c>, one stage later - so the frame actually
  /// posed leads the one written here by <c>30 * dt</c>. That is a constant lead of a few degrees of
  /// shaft, harmless as long as the frame space is continuous across the wrap.
  /// </para>
  /// </summary>
  /// <param name="reverse">
  /// Set when the clip's own shaft is keyframed turning the opposite way to the axle. A cycle that
  /// rotates its shaft element through +360 plays with the axle; one authored the other way, or a
  /// machine whose gearing reverses the sense, needs the frame run backwards or the two shafts turn
  /// against each other - obvious the moment a vanilla axle is placed alongside.
  /// </param>
  public static void LockFrameToAngle(
    AnimationUtil? animUtil,
    string animCode,
    float angleRad,
    bool reverse = false
  ) {
    if (reverse)
      angleRad = -angleRad;
    if (animUtil?.animator?.GetAnimationState(animCode) is not { } state)
      return;
    if (state.Animation == null)
      return;
    state.CurrentFrame = FrameFromAngle(
      angleRad,
      state.Animation.QuantityFrames
    );
  }
}

using ExpandedLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Shared keyframe-driven piston sound logic for the engines and their sub-machines. Both run
/// the same equal-length cycle animation with the piston at its top at <see cref="UpFrame"/>
/// and bottom at <see cref="DownFrame"/>; this fires an airy swoosh once per up-stroke and a
/// heavy metal impact once per down-stroke, detected by watching the cycle animation's current
/// frame cross those thresholds (client-side, so each stroke is heard in step with the locally
/// rendered animation).
/// </summary>
public static class PistonCycleSounds
{
  /// <summary>Cycle frame where the piston reaches the top of its stroke (torch un-equip whoosh).
  /// The shape lifts the piston to its highest offset (+7) here.</summary>
  public const int UpFrame = 45;

  /// <summary>Cycle frame where the piston reaches the bottom of its stroke (anvil merge clang).
  /// The shape drops the piston to its lowest offset (-6) here.</summary>
  public const int DownFrame = 15;

  /// <summary>
  /// Plays the stroke sounds for any thresholds the cycle animation crossed between
  /// <paramref name="lastFrame"/> and <paramref name="currentFrame"/> this tick (handling the
  /// loop wrap), at <paramref name="pos"/>. <paramref name="volumeMul"/> scales the stroke
  /// loudness (and carry range) so a hard-driven engine sounds louder and more violent.
  /// </summary>
  public static void Fire(
    IWorldAccessor world,
    BlockPos pos,
    float lastFrame,
    float currentFrame,
    int totalFrames,
    float volumeMul = 1f
  )
  {
    if (Crossed(lastFrame, currentFrame, totalFrames, UpFrame))
      ExSounds.PlayLocal(
        world,
        pos,
        ExSounds.TorchUnequip,
        1.5f * volumeMul,
        16f * volumeMul,
        true
      );
    if (Crossed(lastFrame, currentFrame, totalFrames, DownFrame))
      ExSounds.PlayLocal(
        world,
        pos,
        ExSounds.AnvilMergeHit,
        0.2f * volumeMul,
        16f * volumeMul,
        true
      );
  }

  /// <summary>True if the cycle animation crossed the piston's top-of-stroke frame
  /// (<see cref="UpFrame"/>) this tick — the moment the cylinder vents its spent steam.</summary>
  public static bool CrossedUpStroke(float last, float cur, int totalFrames) =>
    Crossed(last, cur, totalFrames, UpFrame);

  /// <summary>True when the cycle animation swept across <paramref name="frame"/> this tick
  /// (loop-aware) — for sub-machines that watch their own keyframes rather than the shared
  /// engine up/down strokes (e.g. the air blower's top-of-intake and bottom-of-compression).</summary>
  public static bool CrossedFrame(
    float last,
    float cur,
    int totalFrames,
    int frame
  ) => Crossed(last, cur, totalFrames, frame);

  /// <summary>
  /// True when <paramref name="threshold"/> falls in the half-open interval the frame swept
  /// from <paramref name="last"/> to <paramref name="cur"/> this tick, accounting for the
  /// animation looping back to 0.
  /// </summary>
  private static bool Crossed(
    float last,
    float cur,
    int totalFrames,
    float threshold
  )
  {
    if (totalFrames <= 1)
      return false;
    if (cur >= last) // normal advance within the loop
      return last < threshold && threshold <= cur;
    // Wrapped past the end this tick: crossed if the threshold is after `last` or up to `cur`.
    return threshold > last || threshold <= cur;
  }
}

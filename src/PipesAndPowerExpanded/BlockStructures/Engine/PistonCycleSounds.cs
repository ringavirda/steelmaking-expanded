using ExpandedLib.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Shared keyframe-driven piston sounds for the engines and sub-machines. Both run the same cycle
/// animation (piston top at <see cref="UpFrame"/>, bottom at <see cref="DownFrame"/>); this fires
/// a swoosh per up-stroke and a metal impact per down-stroke as the cycle frame crosses those
/// thresholds (client-side, so each stroke matches the locally rendered animation).
/// </summary>
public static class PistonCycleSounds
{
  /// <summary>Cycle frame where the piston tops out (torch un-equip whoosh).</summary>
  public const int UpFrame = 45;

  /// <summary>Cycle frame where the piston bottoms out (anvil merge clang).</summary>
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

  /// <summary>True if the cycle crossed the top-of-stroke frame (<see cref="UpFrame"/>) this tick -
  /// when the cylinder vents its spent steam.</summary>
  public static bool CrossedUpStroke(float last, float cur, int totalFrames) =>
    Crossed(last, cur, totalFrames, UpFrame);

  /// <summary>True when the cycle swept across <paramref name="frame"/> this tick (loop-aware) -
  /// for sub-machines watching their own keyframes (e.g. the air blower's intake/compression).</summary>
  public static bool CrossedFrame(
    float last,
    float cur,
    int totalFrames,
    int frame
  ) => Crossed(last, cur, totalFrames, frame);

  /// <summary>
  /// True when <paramref name="threshold"/> falls in the half-open interval the frame swept
  /// forward from <paramref name="last"/> to <paramref name="cur"/> this tick, accounting for the
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

    // Distance travelled forward this tick, measured on the loop so a wrap past the end is just a
    // small positive delta.
    float delta = cur - last;
    if (delta < 0f)
      delta += totalFrames;

    // A cycle running backwards - a reversed mechanical network, or a sub-machine phase-lock that
    // writes the frame directly and can jump back - also arrives as cur < last, and is
    // indistinguishable from a wrap by sign alone. It shows up here as a delta close to a whole
    // loop, which no real 50 ms tick covers. Reading those as wraps fired nearly every keyframe on
    // nearly every tick, which is what exhausted the game's concurrent-sound cap.
    if (delta <= 0f || delta > totalFrames / 2f)
      return false;

    // Where the threshold sits ahead of `last`, on the loop.
    float offset = threshold - last;
    if (offset < 0f)
      offset += totalFrames;

    return offset > 0f && offset <= delta;
  }
}

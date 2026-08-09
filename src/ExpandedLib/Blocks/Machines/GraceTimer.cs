using System;
using Vintagestory.API.Datastructures;

namespace ExpandedLib.Blocks.Machines;

/// <summary>
/// A reusable "hold a condition for N seconds, then fire once" accumulator - the hysteresis idiom
/// repeated across the mod's machines (boiler over-pressure / choke, engine over-pressure, pipe
/// burst grace). Accumulates while the condition holds, resets the moment it lifts, and fires a
/// single <c>true</c> when the threshold is crossed (then resets so it re-arms).
///
/// <code>
/// if (_overPressure.Update(pressure > Limit, dt, GraceSeconds))
///     Explode();
/// </code>
/// </summary>
public struct GraceTimer {
  /// <summary>Seconds the condition has held continuously (0 when not counting).</summary>
  public float Elapsed { get; private set; }

  /// <summary>
  /// Advances the timer. While <paramref name="active"/> is <c>true</c> it accrues
  /// <paramref name="dt"/>; once <see cref="Elapsed"/> reaches <paramref name="threshold"/> it
  /// returns <c>true</c> once and resets. Any tick with <paramref name="active"/> <c>false</c>
  /// resets it. <paramref name="threshold"/> is passed per-call so a config reload takes effect
  /// without the timer caching a stale limit.
  /// </summary>
  public bool Update(bool active, float dt, float threshold) {
    if (!active) {
      Elapsed = 0f;
      return false;
    }

    Elapsed += dt;
    if (Elapsed >= threshold) {
      Elapsed = 0f;
      return true;
    }
    return false;
  }

  /// <summary>Clears the accumulator without firing.</summary>
  public void Reset() => Elapsed = 0f;

  /// <summary>Whether the timer is part-way through counting (for HUD "warning" cues).</summary>
  public readonly bool IsCounting => Elapsed > 0f;

  /// <summary>Seconds left before <paramref name="threshold"/> would fire (for HUD countdowns).</summary>
  public readonly float Remaining(float threshold) =>
    Math.Max(0f, threshold - Elapsed);

  /// <summary>Persists the elapsed time under <paramref name="key"/> so a grace survives reload.</summary>
  public readonly void ToTree(ITreeAttribute tree, string key) =>
    tree.SetFloat(key, Elapsed);

  /// <summary>Restores the elapsed time written by <see cref="ToTree"/>.</summary>
  public void FromTree(ITreeAttribute tree, string key) =>
    Elapsed = tree.GetFloat(key);
}

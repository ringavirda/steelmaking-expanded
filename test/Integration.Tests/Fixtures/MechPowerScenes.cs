using System.Collections;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Helpers for headless mechanical-power (MP) tests. The vanilla <see cref="MechanicalNetwork"/> has a
/// public parameterless ctor with settable <c>Speed</c>/<c>NetworkResistance</c>, so a turning (or
/// stalled, or overloaded) network can be faked directly, then bound onto a real
/// <see cref="BEBehaviorMPBase"/> behavior (its <c>network</c> field) and that behavior attached to a
/// block entity's behavior list so <c>GetBehavior&lt;BEBehaviorMPBase&gt;()</c> finds it - the wiring
/// the game does at chunk load but the headless harness skips.
/// </summary>
internal static class MechPower {
  /// <summary>A fake mechanical network turning at <paramref name="speed"/> with the given load.</summary>
  public static MechanicalNetwork Network(float speed, float resistance = 0f) =>
    new() { Speed = speed, NetworkResistance = resistance };

  /// <summary>
  /// Binds <paramref name="network"/> onto <paramref name="behavior"/>'s private <c>network</c> field
  /// and attaches the behavior to <paramref name="be"/> so the BE's <c>GetBehavior</c> resolves it.
  /// </summary>
  public static T Attach<T>(
    BlockEntity be,
    T behavior,
    MechanicalNetwork? network
  )
    where T : BEBehaviorMPBase {
    if (network != null)
      ReflectionHelpers.SetField(behavior, "network", network);
    var list = (IList)ReflectionHelpers.GetField(be, "Behaviors")!;
    list.Add(behavior);
    return behavior;
  }
}

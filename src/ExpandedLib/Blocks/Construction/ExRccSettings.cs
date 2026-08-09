using System;
using System.Collections.Generic;

namespace ExpandedLib.Blocks.Construction;

/// <summary>
/// Player-tunable settings for the right-click construction system, supplied by each mod from its own
/// config. The shared <see cref="ExRightClickConstructable"/> behaviour lives in exlib and so can't
/// reference a mod's <c>*Values</c> accessor directly; instead a mod registers a live getter here keyed
/// by its domain (mod id), and the behaviour resolves the value at break time by the broken block's
/// <c>Code.Domain</c>. Because the getter reads the config every call, a <c>/exmod config</c> change
/// applies immediately. exlib has a single runtime type identity, so this registry is shared across all
/// dependent mods.
/// </summary>
public static class ExRccSettings {
  private static readonly Dictionary<string, Func<float>> _brokenDropsRatios =
    new();

  /// <summary>
  /// Registers the salvage fraction (0..1) for broken RCC mega-blocks of <paramref name="domain"/> -
  /// the share of the consumed construction materials scattered on break. Mods call this at startup
  /// pointing at their config accessor (e.g. <c>() =&gt; PpexValues.RccBrokenDropsRatio</c>).
  /// </summary>
  public static void RegisterBrokenDropsRatio(
    string domain,
    Func<float> ratio
  ) => _brokenDropsRatios[domain] = ratio;

  /// <summary>
  /// The configured salvage fraction for <paramref name="domain"/>, or <c>null</c> when no mod
  /// registered one - in which case the behaviour keeps the JSON/default <c>brokenDropsRatio</c>.
  /// </summary>
  public static float? BrokenDropsRatio(string domain) =>
    _brokenDropsRatios.TryGetValue(domain, out Func<float>? getter)
      ? getter()
      : null;
}

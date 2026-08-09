using System.Collections.Generic;
using ExpandedLib.Registries.Preferences;
using PipesAndPowerExpanded.Helpers;

namespace PipesAndPowerExpanded.Preferences;

/// <summary>
/// The metric/imperial display-unit preference. Adapts the <see cref="ExMeasure"/> formatter to the
/// library's generic <see cref="IExPreference"/> contract: <see cref="Apply"/> sets the active
/// <see cref="ExMeasure.System"/> that every block-info / handbook formatter reads. The simulation
/// always runs in metric; this only changes how values are displayed. Registered (and persisted)
/// through the library's preferences store; its <c>.exmod measure</c> sub-command is built by
/// <see cref="Commands.MeasureSubCommand"/>.
/// <para>
/// Lang keys (ppex domain): <c>command-measure-desc</c>, <c>pref-measure-label</c>,
/// <c>pref-measure-metric</c>, <c>pref-measure-imperial</c>.
/// </para>
/// </summary>
[PreferenceRegister]
public sealed class MeasurePreference : IExPreference {
  public string Key => "measure";

  public IReadOnlyList<string> Options { get; } = ["metric", "imperial"];

  public string Default => "metric";

  public void Apply(string value) => ExMeasure.System = ExMeasure.Parse(value);
}

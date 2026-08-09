using System;
using System.Collections.Generic;

namespace ExpandedLib.Registries.Config;

/// <summary>
/// Process-wide registry of the config stores mods expose to the generic <c>/exmod config</c> command,
/// keyed by mod id. A mod opts a config in with <c>[ExConfigRegister(..., Manageable = true)]</c>; the
/// source-generated accessor registers its store here from its <c>Load</c>. Mirrors
/// <see cref="Recipes.ExRecipeProfiles"/> and lives in exlib so any dependent mod can plug in.
/// </summary>
public static class ExConfigProfiles {
  private static readonly Dictionary<string, IExConfigAccess> _configs = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>Registers (or replaces) a mod's manageable config store. Called from the generated
  /// accessor's <c>Load</c> for any config marked <c>Manageable</c>.</summary>
  public static void Register(IExConfigAccess config) =>
    _configs[config.ModId] = config;

  /// <summary>Looks up a registered config by mod id (case-insensitive).</summary>
  public static bool TryGet(string code, out IExConfigAccess config) =>
    _configs.TryGetValue(code, out config!);

  /// <summary>The registered mod ids, for listing in the command.</summary>
  public static IReadOnlyCollection<string> Codes => _configs.Keys;
}

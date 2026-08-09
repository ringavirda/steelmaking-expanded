using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Preferences;

/// <summary>
/// Reflection-driven preference registration for mods built on ExpandedLib, the preference-side
/// counterpart to <see cref="Commands.CommandRegistry"/>. Scans an assembly for
/// <see cref="IExPreference"/> classes carrying <see cref="PreferenceRegisterAttribute"/> and adds
/// each one to <see cref="ExPreferences"/>, so a mod system never hand-wires preferences.
/// </summary>
public static class PreferenceRegistry {
  /// <summary>
  /// Registers every <see cref="PreferenceRegisterAttribute"/>-decorated
  /// <see cref="IExPreference"/> in <paramref name="asm"/> (default: the calling mod's own
  /// assembly) with <see cref="ExPreferences"/>. Call from <c>ModSystem.StartClientSide</c>
  /// after <see cref="ExPreferences.LoadConfig"/> and before the <c>.exmod</c> command registers,
  /// since the command builds a sub-command per registered preference.
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      var attr = type.GetCustomAttribute<PreferenceRegisterAttribute>();
      if (attr == null)
        continue;

      if (!typeof(IExPreference).IsAssignableFrom(type)) {
        api.Logger.Warning(
          "[{0}] PreferenceRegistry: {1} has [PreferenceRegister] but does not implement IExPreference; skipped.",
          modId,
          type.FullName
        );
        continue;
      }

      var pref = (IExPreference)Activator.CreateInstance(type)!;
      ExPreferences.Register(pref);
    }
  }
}

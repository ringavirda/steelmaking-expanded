using System.Collections.Generic;
using ExpandedLib.Helpers;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Molds;

/// <summary>
/// Central availability gate for the casting molds this mod adds (the plate, double-ingot and
/// quad-rod tool molds). A server admin can disable any of them - they shortcut the early-game
/// plate/rod progression that would otherwise need smithing or a helve hammer - through
/// <c>/exmod molds &lt;plate|ingot|rod|all&gt; &lt;on|off&gt;</c>, which flips the matching
/// <see cref="SmexConfig"/> flag and persists it.
/// <para>
/// A disabled mold is, on the next world load, stripped of its clay-forming recipe and hidden from
/// creative and the handbook (see <see cref="ApplyDisables"/>); any already-placed mold stops
/// yielding its casting immediately (the tool-mold patches consult <see cref="IsToolMoldDisabled"/>).
/// </para>
/// </summary>
public static class MoldGating {
  /// <summary>The user-facing toggle keys, mapped to the block's <c>tooltype</c> variant.</summary>
  private static readonly Dictionary<string, string> KeyToToolType = new() {
    ["plate"] = "plate",
    ["ingot"] = "doubleingot",
    ["rod"] = "quadrod",
  };

  /// <summary>The toggle keys accepted by the command (plus <c>all</c>, handled by the command).</summary>
  public static IReadOnlyCollection<string> Keys => KeyToToolType.Keys;

  /// <summary>Whether the mold identified by <paramref name="key"/> (plate/ingot/rod) is enabled.</summary>
  public static bool IsEnabled(string key) =>
    key switch {
      "plate" => SmexValues.EnablePlateMold,
      "ingot" => SmexValues.EnableIngotMold,
      "rod" => SmexValues.EnableRodMold,
      _ => true,
    };

  /// <summary>Sets the enabled flag for <paramref name="key"/> and persists the config.</summary>
  public static void SetEnabled(string key, bool enabled) =>
    SmexValues.Edit(c => {
      switch (key) {
        case "plate":
          c.EnablePlateMold = enabled;
          break;
        case "ingot":
          c.EnableIngotMold = enabled;
          break;
        case "rod":
          c.EnableRodMold = enabled;
          break;
      }
    });

  /// <summary>
  /// Whether <paramref name="code"/> is one of this mod's tool molds whose type is currently
  /// disabled. Matches both the raw and fired variants by their trailing <c>tooltype</c>.
  /// </summary>
  public static bool IsToolMoldDisabled(AssetLocation? code) {
    if (code is not { Domain: "smex" } || !code.Path.StartsWith("toolmold"))
      return false;

    if (code.Path.EndsWith("-plate"))
      return !SmexValues.EnablePlateMold;
    if (code.Path.EndsWith("-doubleingot"))
      return !SmexValues.EnableIngotMold;
    if (code.Path.EndsWith("-quadrod"))
      return !SmexValues.EnableRodMold;
    return false;
  }

  /// <summary>
  /// Enforces the disabled molds at world load - the policy layer over <see cref="ExContentGate"/>:
  /// for each disabled type, strips its clay-forming recipe and hides both its raw and fired blocks
  /// from creative + the handbook. Idempotent and safe to run on either side; call once per side
  /// after recipes have loaded.
  /// </summary>
  public static void ApplyDisables(ICoreAPI api) {
    foreach (var (key, toolType) in KeyToToolType) {
      if (IsEnabled(key))
        continue;

      int removed = ExContentGate.RemoveClayformingRecipes(
        api,
        code => code.Domain == "smex" && code.Path.Contains("raw-" + toolType)
      );
      ExContentGate.HideFromCreativeAndHandbook(
        api,
        obj =>
          obj.Code is { Domain: "smex" } c
          && c.Path.StartsWith("toolmold")
          && c.Path.EndsWith("-" + toolType)
      );

      api.Logger.Notification(
        "[smex] Mold '{0}' disabled by config: removed {1} clay-forming recipe(s), hidden from creative/handbook.",
        key,
        removed
      );
    }
  }
}

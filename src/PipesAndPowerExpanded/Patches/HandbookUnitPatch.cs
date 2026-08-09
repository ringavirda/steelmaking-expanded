using HarmonyLib;
using PipesAndPowerExpanded.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.Patches;

/// <summary>
/// Makes handbook page text follow the active <see cref="ExMeasure.System"/>: plain metric unit
/// mentions in the prose ("30 L", "2-4 atm", "160-220 °C") are converted to the chosen system just
/// before the page builds its rich-text components, so existing metric handbook text reads in
/// imperial without being rewritten. Hooks the survival handbook text page's <c>Init</c> (where the
/// <c>Text</c> field is turned into the displayed components).
/// <para>
/// Only runs in imperial mode (metric is the authored text, left untouched). The page may store the
/// lang <em>key</em> in <c>Text</c> and resolve it inside <c>Init</c>, so we resolve it ourselves
/// via <see cref="Lang.Get(string,object[])"/> first - re-resolving an already-resolved string is a
/// no-op. The conversion is applied every time a page is initialised: the handbook builds fresh
/// <see cref="GuiHandbookTextPage"/> objects (each holding the lang key in <c>Text</c>) on every
/// <c>loadEntries</c>, so re-running it picks up the now-active system and reverts cleanly to metric.
/// The page Init only runs at load, so <see cref="Rebuild"/> forces that rebuild when the player
/// switches units mid-session via <c>.exmod measure</c>. The look-at HUD updates instantly.
/// </para>
/// </summary>
[HarmonyPatch(typeof(GuiHandbookTextPage), "Init")]
public static class HandbookUnitPatch {
  /// <summary>
  /// Rebuilds the survival handbook's pages so their prose re-converts to the current
  /// <see cref="ExMeasure.System"/>. Page text is only converted in <c>Init</c>, which runs when the
  /// handbook (re)loads its entries; nothing re-runs it on a unit change, so we trigger the same
  /// <c>loadEntries</c> path the built-in <c>.debug reloadhandbook</c> command uses. Reflection is
  /// used because the dialog instance and method are not public; a failure just leaves the handbook
  /// on its previous units rather than disrupting the command.
  /// </summary>
  public static void Rebuild(ICoreClientAPI capi) {
    try {
      var handbook = capi.ModLoader.GetModSystem<ModSystemSurvivalHandbook>();
      if (handbook == null)
        return;

      object? dialog = Traverse.Create(handbook).Field("dialog").GetValue();
      if (dialog == null)
        return;

      // loadEntries() clears and re-creates every page from config/handbook, re-running each page's
      // Init (and this patch) for the now-active unit system.
      Traverse.Create(dialog).Method("loadEntries").GetValue();
    } catch {
      // Never let a display refresh break the measure command.
    }
  }

  // Prefix so the text is converted before Init builds it into display components.
  public static void Prefix(GuiHandbookTextPage __instance) {
    if (ExMeasure.System != MeasurementSystem.Imperial)
      return;

    try {
      var field = Traverse.Create(__instance).Field("Text");
      string? raw = field.GetValue<string>();
      if (string.IsNullOrEmpty(raw))
        return;

      // Resolve in case Text still holds the lang key (Init would otherwise resolve it itself).
      string resolved = Lang.Get(raw);
      string converted = ExMeasure.ConvertMetricText(resolved);
      if (converted != raw)
        field.SetValue(converted);
    } catch {
      // Never let a display tweak break the handbook; fall back to the authored metric text.
    }
  }
}

using System.Collections.Generic;
using ExpandedLib.Registries.Config;
using ExpandedLib.Registries.Recipes;

namespace PipesAndPowerExpanded;

/// <summary>
/// The steam-machine recipe cost catalogue, written to <c>ModConfig/ppex_recipes.json</c> alongside
/// the main <c>ppex_values.json</c>. Each entry names a grid recipe (by output) or RCC construction (by
/// block) to manage and its ingredient totals per cost level. The <c>normal</c> level is auto-filled
/// from the recipes as authored on first run, and any level a recipe doesn't pin is filled by scaling
/// <c>normal</c> (so the whole "cheap" tier appears as explicit, editable numbers). Players/admins can
/// then set any specific number. The active level is chosen by <c>/exmod steam &lt;level&gt;</c>
/// (stored in <see cref="PpexConfig.RecipeLevel"/>) and applied on the next world reload.
/// </summary>
[ExConfigRegister("ppex_recipes.json", "ppex")]
public class PpexRecipeConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }

  private Dictionary<string, RecipeCostEntry>? _recipes;

  /// <summary>Never null: a missing or null <c>Recipes</c> in the file falls back to the code
  /// defaults (a wrong edit can't blank the catalogue). Missing/broken individual entries are
  /// repaired against <see cref="DefaultCatalogue"/> at load by <see cref="ExRecipeCosts.Reconcile"/>.</summary>
  public Dictionary<string, RecipeCostEntry> Recipes {
    get => _recipes ??= Defaults();
    set => _recipes = value;
  }

  /// <summary>A fresh copy of the shipped catalogue defaults, used to repair the loaded file.</summary>
  public static Dictionary<string, RecipeCostEntry> DefaultCatalogue() =>
    Defaults();

  private static RecipeCostEntry Rcc(string match) =>
    new() { Type = "rcc", Match = match };

  private static RecipeCostEntry Grid(string match) =>
    new() { Type = "grid", Match = match };

  // Grid recipe with a pinned cheap output count (the pipe straight/bend/junction families craft double
  // in the cheap profile). Authored output: straight 2, bend/t/x-junction 1 - so cheap doubles to 4/2/2/2.
  private static RecipeCostEntry GridOut(string match, int cheapOutput) =>
    new() {
      Type = "grid",
      Match = match,
      Profiles = new() { ["cheap"] = new() { Quantity = cheapOutput } },
    };

  // Curated list of every grid + RCC recipe this mod ships (kept in sync by hand, mirroring the RCC
  // style). Profiles are auto-filled at load: the normal baseline is read from the live recipe and the
  // cheap profile is scaled (half cost), both editable in the file. Only the cheap pipe output is pinned.
  private static Dictionary<string, RecipeCostEntry> Defaults() =>
    new() {
      // RCC constructions (the heavy multiblock build costs).
      ["boilercornish-rcc"] = Rcc("ppex:boilercornish-*"),
      ["boilerlancashire-rcc"] = Rcc("ppex:boilerlancashire-*"),
      ["enginecornish-rcc"] = Rcc("ppex:enginecornish-*"),
      ["enginewatt-rcc"] = Rcc("ppex:enginewatt-*"),

      // Machine grid "frame" recipes.
      ["boilercornish-grid"] = Grid("ppex:boilercornish-*"),
      ["boilerlancashire-grid"] = Grid("ppex:boilerlancashire-*"),
      ["enginecornish-grid"] = Grid("ppex:enginecornish-*"),
      ["enginewatt-grid"] = Grid("ppex:enginewatt-*"),
      ["enginefluidpump-grid"] = Grid("ppex:enginefluidpump-*"),
      ["enginempgenerator-grid"] = Grid("ppex:enginempgenerator-*"),
      ["manualfluidpump-grid"] = Grid("ppex:manualfluidpump-*"),
      ["steamcondenser-grid"] = Grid("ppex:steamcondenser-*"),

      // Pipe grid recipes - straight/bend/junctions yield double in cheap.
      ["pipe-straight-grid"] = GridOut("ppex:pipe-straight-*", 4),
      ["pipe-bend-grid"] = GridOut("ppex:pipe-bend-*", 2),
      ["pipe-tjunction-grid"] = GridOut("ppex:pipe-tjunction-*", 2),
      ["pipe-xjunction-grid"] = GridOut("ppex:pipe-xjunction-*", 2),
      ["pipe-fluidintake-grid"] = Grid("ppex:pipe-fluidintake-*"),
      ["pipe-outlet-grid"] = Grid("ppex:pipe-outlet-*"),
      ["pipe-passthrough-grid"] = Grid("ppex:pipe-passthrough-*"),
      ["pipe-passthroughbend-grid"] = Grid("ppex:pipe-passthroughbend-*"),
      ["pipe-valve-grid"] = Grid("ppex:pipe-valve-*"),
      ["pipe-pressurevalve-grid"] = Grid("ppex:pipe-pressurevalve-*"),
    };
}

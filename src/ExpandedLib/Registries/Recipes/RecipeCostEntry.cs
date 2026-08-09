using System.Collections.Generic;

namespace ExpandedLib.Registries.Recipes;

/// <summary>
/// One managed recipe in a mod's cost catalogue: its kind (how the framework finds and edits it), the
/// wildcard code that locates it, and a self-contained <see cref="RecipeProfileCost"/> per cost profile
/// (e.g. <c>normal</c>/<c>cheap</c>). The <c>normal</c> profile is auto-filled from the live recipe on
/// first run (see <see cref="ExRecipeCosts.EnsureNormalExtracted"/>); a mod ships only what an alternate
/// profile pins, and the rest is scale-filled. Players can edit any number afterwards.
/// </summary>
public class RecipeCostEntry {
  /// <summary>How to locate and edit the recipe: <c>"grid"</c> (a crafting recipe, matched by output
  /// code) or <c>"rcc"</c> (a right-click-construction block, matched by block code).</summary>
  public string Type { get; set; } = "grid";

  /// <summary>Wildcard code matched against the grid output / RCC block code (e.g.
  /// <c>"ppex:enginewatt-*"</c>). Kept separate from the catalogue key so the same block can have
  /// both a grid entry and an rcc entry under distinct keys.</summary>
  public string Match { get; set; } = "";

  /// <summary>Profile name (e.g. <c>"normal"</c>, <c>"cheap"</c>) → everything that profile changes
  /// for this recipe. Self-contained: a profile holds its own ingredient costs, RCC stage costs and
  /// output quantity, so switching profile is just picking one entry here.</summary>
  public Dictionary<string, RecipeProfileCost> Profiles { get; set; } = new();
}

using System.Collections.Generic;

namespace ExpandedLib.Registries.Recipes;

/// <summary>
/// A mod's registration with the shared recipe-cost framework: everything <see cref="ExRecipeProfiles"/>
/// needs to read, fill, persist and apply that mod's cost catalogue, plus get/set the active level the
/// <c>/exmod recipes &lt;code&gt; &lt;level&gt;</c> command flips. A mod registers one of these in its
/// <c>Start</c> so the command and the library's apply pass can find it by <see cref="Code"/>.
/// </summary>
public sealed class RecipeProfile {
  /// <summary>The mod's short code used on the command line, e.g. <c>"smex"</c> in
  /// <c>/exmod recipes smex cheap</c>.</summary>
  public required string Code { get; init; }

  /// <summary>The live, persisted catalogue this profile manages (e.g. <c>SmexRecipeValues.Recipes</c>).</summary>
  public required System.Func<
    IDictionary<string, RecipeCostEntry>
  > Catalogue { get; init; }

  /// <summary>A fresh copy of the mod's shipped catalogue defaults, used to repair a hand-edited file.</summary>
  public required System.Func<
    IReadOnlyDictionary<string, RecipeCostEntry>
  > Defaults { get; init; }

  /// <summary>Reads the mod's currently selected cost level (e.g. <c>SmexValues.RecipeLevel</c>).</summary>
  public required System.Func<string> GetLevel { get; init; }

  /// <summary>Sets and persists the mod's selected cost level (applied on the next world reload).</summary>
  public required System.Action<string> SetLevel { get; init; }

  /// <summary>Persists the catalogue file after the framework fills in its auto-derived entries/levels.</summary>
  public required System.Action SaveCatalogue { get; init; }

  /// <summary>The selectable level names, in display order. The first is the authored baseline.</summary>
  public IReadOnlyList<string> Levels { get; init; } = ["normal", "cheap"];

  /// <summary>Levels filled by scaling <c>normal</c>: level name → factor (e.g. <c>cheap = 0.5</c> =
  /// half cost). Any level a recipe pins explicitly is left alone.</summary>
  public IReadOnlyDictionary<string, double> DerivedLevels { get; init; } =
    new Dictionary<string, double> { ["cheap"] = 0.5 };
}

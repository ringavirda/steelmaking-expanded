using System.Collections.Generic;

namespace ExpandedLib.Registries.Recipes;

/// <summary>
/// Everything one cost profile (e.g. <c>cheap</c>) changes for a single recipe - self-contained, so a
/// profile is the complete picture of that recipe at that cost level. A grid recipe uses
/// <see cref="Ingredients"/> (and optionally <see cref="Quantity"/> to change how many it yields); an
/// RCC construction uses <see cref="Stages"/>. The fields not relevant to the recipe's kind are left
/// null.
/// </summary>
public class RecipeProfileCost {
  /// <summary>Grid recipes: ingredient code → quantity required.</summary>
  public Dictionary<string, int>? Ingredients { get; set; }

  /// <summary>Grid recipes: the crafted output count. Null keeps the recipe's authored output (most
  /// recipes); set it to override (e.g. cheap pipes yield double).</summary>
  public int? Quantity { get; set; }

  /// <summary>RCC constructions: stage index (as a string) → that stage's require-stacks
  /// (ingredient <c>name</c>, falling back to its code, → quantity). Each construction stage is
  /// editable on its own.</summary>
  public Dictionary<string, Dictionary<string, int>>? Stages { get; set; }

  /// <summary>True when this profile carries any cost data worth applying or persisting.</summary>
  public bool HasContent =>
    Quantity.HasValue
    || (Ingredients is { Count: > 0 })
    || (Stages is { Count: > 0 });
}

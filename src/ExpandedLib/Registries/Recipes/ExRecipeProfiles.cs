using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Recipes;

/// <summary>
/// The shared anchor point for mods that expose recipe-cost levels (<c>normal</c>/<c>cheap</c>/...): a
/// process-wide registry of <see cref="RecipeProfile"/>s keyed by mod code. It owns the one apply
/// pipeline every mod shares (repair → discover → fill levels → persist → apply), so a dependent mod
/// only registers its catalogue and the generic <c>/exmod recipes &lt;code&gt; &lt;level&gt;</c> command
/// (and exlib's load-time apply) drive it. Lives in exlib so any mod - not just ppex/smex - can plug in.
/// </summary>
public static class ExRecipeProfiles {
  private static readonly Dictionary<string, RecipeProfile> _profiles = new(
    StringComparer.OrdinalIgnoreCase
  );

  /// <summary>Registers (or replaces) a mod's profile. Call once from the mod's <c>Start</c>, after its
  /// config/catalogue stores have loaded.</summary>
  public static void Register(RecipeProfile profile) =>
    _profiles[profile.Code] = profile;

  /// <summary>Looks up a registered profile by mod code (case-insensitive).</summary>
  public static bool TryGet(string code, out RecipeProfile profile) =>
    _profiles.TryGetValue(code, out profile!);

  /// <summary>The registered mod codes, for listing in the command.</summary>
  public static IReadOnlyCollection<string> Codes => _profiles.Keys;

  /// <summary>Runs the apply pipeline for every registered profile. exlib calls this from its
  /// <c>StartServerSide</c>/<c>StartClientSide</c> (after all mods registered in their <c>Start</c>),
  /// so the active level is applied to the live recipes on each world load.</summary>
  public static void ApplyAll(ICoreAPI api) {
    foreach (var profile in _profiles.Values)
      Apply(api, profile);
  }

  /// <summary>
  /// The shared per-profile pipeline: repair the catalogue against the mod's curated defaults, fill the
  /// <c>normal</c> baseline from the live recipes and the derived levels by scaling it, persist if
  /// anything changed (server only - the file is host-authoritative), then apply the selected level to
  /// the live grid/RCC recipes.
  /// </summary>
  public static void Apply(ICoreAPI api, RecipeProfile profile) {
    var live = profile.Catalogue();

    bool changed = ExRecipeCosts.Reconcile(live, profile.Defaults());
    changed |= ExRecipeCosts.EnsureNormalExtracted(api, live);
    foreach (var (level, factor) in profile.DerivedLevels)
      changed |= ExRecipeCosts.EnsureScaledLevel(live, level, factor);

    // The catalogue is server-authoritative; the client re-derives in memory for its handbook but
    // must not write the shared single-player file (it would clobber the server's grid entries).
    if (changed && api.Side == EnumAppSide.Server)
      profile.SaveCatalogue();

    ExRecipeCosts.Apply(api, live, profile.GetLevel());
  }
}

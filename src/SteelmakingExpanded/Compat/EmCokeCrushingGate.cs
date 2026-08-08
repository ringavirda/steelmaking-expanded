using System.Linq;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Compat;

/// <summary>
/// Gates Expanded Matter's hammer-on-coke grid recipe behind
/// <see cref="SmexValues.EnableEmCokeCrushing"/>.
/// <para>
/// EM ships three crushing recipes that all output <c>em:crushed-ore-coal</c>: charcoal to one,
/// coal ore to two, and coke to four. Only the coke one is smex's business - coke is smex's
/// product, and turning one lump into four crushed coal is worth about double its own fuel
/// duration, which undercuts the chain the mod is built around. The other two are EM's own
/// economy and are left alone.
/// </para>
/// <para>
/// This used to be a JSON patch pinning <c>/2/enabled</c> to false, which was both unconditional
/// and positional - it silently targeted whatever recipe happened to sit at index 2 in EM's file.
/// Matching on the ingredient survives EM reordering its own recipes, and going through config
/// lets a server admin opt back in.
/// </para>
/// </summary>
public static class EmCokeCrushingGate
{
  /// <summary>
  /// Removes EM's coke crushing recipe unless the config re-enables it. Idempotent and safe on
  /// either side; call once per side after recipes have resolved.
  /// </summary>
  public static void Apply(ICoreAPI api)
  {
    if (
      SmexValues.EnableEmCokeCrushing
      || !api.ModLoader.IsModEnabled("em")
    )
      return;

    int removed = api.World.GridRecipes.RemoveAll(r =>
      r.Output?.Code is { Domain: "em", Path: "crushed-ore-coal" }
      && r.Ingredients?.Values.Any(i =>
        i?.Code is { Domain: "game", Path: "coke" }
      ) == true
    );

    if (removed > 0)
      api.Logger.Notification(
        "[smex] Removed {0} Expanded Matter coke-crushing grid recipe(s); set "
          + "enableEmCokeCrushing to keep them.",
        removed
      );
  }
}

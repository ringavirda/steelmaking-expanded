using ExpandedLib.Helpers;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Compat;

/// <summary>
/// Retires <c>game:crushed-coke</c> from the visible game.
/// <para>
/// The item is still declared, and deliberately so: an item code that stops resolving takes every
/// existing stack of it with it, so it has to outlive the world it is being migrated out of. What
/// it no longer has is any way to obtain it (its <c>crushingProps</c> source is gone) or any use
/// (the furnace takes whole coke), so leaving it in the creative inventory and the handbook would
/// advertise a dead end. <see cref="BlockMigrations.CrushedCokeMigration"/> converts the stacks
/// themselves as chunks load and as players join.
/// </para>
/// </summary>
public static class CrushedCokeRetirement
{
  /// <summary>Hides the retired item. Idempotent; call once per side after assets have loaded.</summary>
  public static void Apply(ICoreAPI api) =>
    ExContentGate.HideFromCreativeAndHandbook(
      api,
      obj => obj.Code is { Domain: "game", Path: "crushed-coke" }
    );
}

using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Patches;

/// <summary>
/// Adds burden behaviour to the vanilla coal pile without replacing its block-entity class: a
/// pile of burden lit outside a working furnace burns itself out after a fixed time and goes
/// cold, while a pile a blast furnace is managing (drawing air through, and consuming) keeps
/// burning for as long as the campaign runs. Per-pile state lives in a side table keyed by the
/// vanilla block entity, so other mods that touch the coal pile can coexist.
/// <para>
/// Nothing is destroyed here. An unblown charge is burden that never got the air to work, so it is
/// left exactly as it was loaded, ready to be lit again - the furnace, not the pile, decides
/// whether it becomes iron.
/// </para>
/// </summary>
public static class BurdenPiles {
  private sealed class PileState {
    public int BurnTimer;
    public bool Managed;
  }

  private static readonly ConditionalWeakTable<
    BlockEntityCoalPile,
    PileState
  > _states = new();

  /// <summary>
  /// Marks <paramref name="pile"/> as managed (a blast furnace is drawing air through it), which
  /// suspends its burn-out countdown.
  /// </summary>
  public static void SetManagedByFurnace(
    BlockEntityCoalPile pile,
    bool managed
  ) => _states.GetOrCreateValue(pile).Managed = managed;

  /// <summary>
  /// Hands <paramref name="pile"/> back to its own burn clock and puts it out - what a furnace does
  /// to its hearth when it goes out. The burden itself is untouched.
  /// </summary>
  public static void Release(BlockEntityCoalPile pile) {
    SetManagedByFurnace(pile, false);
    BurnOut(pile);
  }

  /// <summary>
  /// Puts a burning pile out and restarts its countdown, so relighting it gives a full burn rather
  /// than dying again on the next tick.
  /// </summary>
  private static void BurnOut(BlockEntityCoalPile pile) {
    _states.GetOrCreateValue(pile).BurnTimer = 0;
    if (pile.IsBurning)
      pile.Extinguish();
  }

  internal static void OnCheckBurn(BlockEntityCoalPile pile) {
    var state = _states.GetOrCreateValue(pile);
    if (state.Managed)
      return;

    if (
      pile.IsBurning
      && pile.inventory != null
      && pile.inventory.Count > 0
      && !pile.inventory[0].Empty
      && pile.inventory[0].Itemstack?.Collectible.Code.Path == "burden"
    ) {
      state.BurnTimer++;
      if (state.BurnTimer >= SmexValues.BurdenBurnTime)
        BurnOut(pile);
    }
  }

  internal static void SaveTo(BlockEntityCoalPile pile, ITreeAttribute tree) =>
    tree.SetInt("burdenBurnTimer", _states.GetOrCreateValue(pile).BurnTimer);

  internal static void LoadFrom(
    BlockEntityCoalPile pile,
    ITreeAttribute tree
  ) =>
    _states.GetOrCreateValue(pile).BurnTimer = tree.GetInt(
      "burdenBurnTimer",
      // Falls back to the pre-rename key so a burning pile keeps its timer across the upgrade.
      tree.GetInt("blastmixBurnTimer", 0)
    );
}

/// <summary>
/// Harmony hooks wiring <see cref="BurdenPiles"/> into the vanilla coal pile's
/// lifecycle: a server-side burn-check tick (registered through the block
/// entity, so it is cleaned up on removal/unload automatically) and persistence
/// of the burn timer.
/// </summary>
[HarmonyPatch(typeof(BlockEntityCoalPile))]
public static class CoalPileBurdenPatches {
  [HarmonyPostfix]
  [HarmonyPatch(nameof(BlockEntityCoalPile.Initialize))]
  public static void InitializePostfix(
    BlockEntityCoalPile __instance,
    ICoreAPI api
  ) {
    if (api.Side == EnumAppSide.Server)
      __instance.RegisterGameTickListener(
        _ => BurdenPiles.OnCheckBurn(__instance),
        1000
      );
  }

  [HarmonyPostfix]
  [HarmonyPatch(nameof(BlockEntityCoalPile.ToTreeAttributes))]
  public static void ToTreePostfix(
    BlockEntityCoalPile __instance,
    ITreeAttribute tree
  ) => BurdenPiles.SaveTo(__instance, tree);

  [HarmonyPostfix]
  [HarmonyPatch(nameof(BlockEntityCoalPile.FromTreeAttributes))]
  public static void FromTreePostfix(
    BlockEntityCoalPile __instance,
    ITreeAttribute tree
  ) => BurdenPiles.LoadFrom(__instance, tree);
}

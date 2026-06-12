using System.Runtime.CompilerServices;
using HarmonyLib;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Patches;

/// <summary>
/// Adds blast-mix behaviour to the vanilla coal pile without replacing its
/// block-entity class: a burning pile of blast mix converts into a
/// solidified-slag block after a fixed time, unless a blast furnace is managing
/// (consuming) it. Per-pile state lives in a side table keyed by the vanilla
/// block entity, so other mods that touch the coal pile can coexist.
/// </summary>
public static class BlastmixPiles
{
  private sealed class PileState
  {
    public int BurnTimer;
    public bool Managed;
  }

  private static readonly ConditionalWeakTable<
    BlockEntityCoalPile,
    PileState
  > _states = new();

  /// <summary>
  /// Marks <paramref name="pile"/> as managed (a blast furnace is consuming it),
  /// which suspends the burn-to-slag countdown.
  /// </summary>
  public static void SetManagedByFurnace(
    BlockEntityCoalPile pile,
    bool managed
  ) => _states.GetOrCreateValue(pile).Managed = managed;

  /// <summary>Replaces a burnt-out blast-mix pile with a solidified-slag block carrying the same count.</summary>
  public static void ConvertToSlag(BlockEntityCoalPile pile)
  {
    if (pile.inventory == null || pile.inventory.Count == 0)
      return;
    if (pile.inventory[0].Empty)
      return;

    int amount = pile.inventory[0].StackSize;
    Block? slagBlock = pile.Api.World.GetBlock(
      new AssetLocation("smex", "slag")
    );
    if (slagBlock == null)
      return;

    pile.Api.World.BlockAccessor.SetBlock(slagBlock.BlockId, pile.Pos);
    if (
      pile.Api.World.BlockAccessor.GetBlockEntity(pile.Pos)
      is BlockEntitySlag beSlag
    )
    {
      beSlag.SlagCount = amount;
      beSlag.MarkDirty(true);
    }
  }

  internal static void OnCheckBurn(BlockEntityCoalPile pile)
  {
    var state = _states.GetOrCreateValue(pile);
    if (state.Managed)
      return;

    if (
      pile.IsBurning
      && pile.inventory != null
      && pile.inventory.Count > 0
      && !pile.inventory[0].Empty
      && pile.inventory[0].Itemstack?.Collectible.Code.Path == "blastmix"
    )
    {
      state.BurnTimer++;
      if (state.BurnTimer >= SmexValues.BlastmixBurnTime)
        ConvertToSlag(pile);
    }
  }

  internal static void SaveTo(BlockEntityCoalPile pile, ITreeAttribute tree) =>
    tree.SetInt("blastmixBurnTimer", _states.GetOrCreateValue(pile).BurnTimer);

  internal static void LoadFrom(
    BlockEntityCoalPile pile,
    ITreeAttribute tree
  ) =>
    _states.GetOrCreateValue(pile).BurnTimer = tree.GetInt(
      "blastmixBurnTimer",
      0
    );
}

/// <summary>
/// Harmony hooks wiring <see cref="BlastmixPiles"/> into the vanilla coal pile's
/// lifecycle: a server-side burn-check tick (registered through the block
/// entity, so it is cleaned up on removal/unload automatically) and persistence
/// of the burn timer.
/// </summary>
[HarmonyPatch(typeof(BlockEntityCoalPile))]
public static class CoalPileBlastmixPatches
{
  [HarmonyPostfix]
  [HarmonyPatch(nameof(BlockEntityCoalPile.Initialize))]
  public static void InitializePostfix(
    BlockEntityCoalPile __instance,
    ICoreAPI api
  )
  {
    if (api.Side == EnumAppSide.Server)
      __instance.RegisterGameTickListener(
        _ => BlastmixPiles.OnCheckBurn(__instance),
        1000
      );
  }

  [HarmonyPostfix]
  [HarmonyPatch(nameof(BlockEntityCoalPile.ToTreeAttributes))]
  public static void ToTreePostfix(
    BlockEntityCoalPile __instance,
    ITreeAttribute tree
  ) => BlastmixPiles.SaveTo(__instance, tree);

  [HarmonyPostfix]
  [HarmonyPatch(nameof(BlockEntityCoalPile.FromTreeAttributes))]
  public static void FromTreePostfix(
    BlockEntityCoalPile __instance,
    ITreeAttribute tree
  ) => BlastmixPiles.LoadFrom(__instance, tree);
}

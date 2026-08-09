using System;
using System.Linq;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>What a chisel + hammer click resolved to on an <see cref="IChiselableMolten"/> holder.</summary>
public enum ChiselOutcome {
  /// <summary>Not a chisel-out here (no chisel + hammer, or nothing solidified) - let the click fall through.</summary>
  NotChiseling,

  /// <summary>The player is chiselling solidified content that isn't ready yet (too hot / too full) - claimed, no recovery.</summary>
  Blocked,

  /// <summary>The hardened content was chipped out and recovered.</summary>
  Chiseled,
}

/// <summary>
/// One implementation of the "chip solidified metal out with a chisel + hammer" interaction, shared by
/// every <see cref="IChiselableMolten"/> holder (canal cells, the molten barrel, the bessemer vessel)
/// so the tool gating, the not-ready feedback, the recovered drop, the tool wear and the sound aren't
/// re-implemented per block. Holders keep only their content-specific state (the interface) and the
/// clear-and-recover step (<see cref="IChiselableMolten.ChiselOut"/>); the drop itself is built with the
/// shared <see cref="BuildRecovery"/>.
/// </summary>
public static class MoltenChisel {
  /// <summary>True when <paramref name="stack"/> is a tool of kind <paramref name="tool"/>.</summary>
  public static bool IsTool(ItemStack? stack, EnumTool tool) =>
    stack?.Collectible?.Tool == tool;

  /// <summary>True when the player holds a chisel in the active hand and a hammer in the off-hand.</summary>
  public static bool HasChiselAndHammer(IPlayer byPlayer) =>
    IsTool(
      byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack,
      EnumTool.Chisel
    ) && IsTool(byPlayer.Entity?.LeftHandItemSlot?.Itemstack, EnumTool.Hammer);

  /// <summary>
  /// The metal-bit recovery stack for <paramref name="units"/> of the metal <paramref name="metalCode"/>
  /// at <paramref name="temperature"/> °C - <paramref name="unitsPerBit"/> units per bit, mapped to the
  /// solid drop code via <see cref="MoltenNetwork.SolidDropLocation"/>. When the solid item doesn't
  /// resolve it falls back to slag (if <paramref name="slagFallback"/>) or returns <c>null</c>. Shared by
  /// every chisel/break drop path so the bit ratio and temperature handling live in one place.
  /// </summary>
  public static ItemStack? BuildRecovery(
    IWorldAccessor world,
    AssetLocation metalCode,
    float temperature,
    int units,
    int unitsPerBit = 5,
    bool slagFallback = false
  ) {
    int count = Math.Max(1, units / unitsPerBit);
    AssetLocation loc = MoltenNetwork.SolidDropLocation(metalCode);
    Item? item = world.GetItem(loc);
    if (item == null) {
      if (!slagFallback)
        return null;
      Item? slag = world.GetItem(new AssetLocation("smex:slag"));
      return slag != null ? new ItemStack(slag, count) : null;
    }
    var drop = new ItemStack(item, count);
    MoltenMetal.SetTemperature(world, drop, temperature);
    return drop;
  }

  /// <summary>
  /// Runs the full chisel-out interaction against <paramref name="target"/>. Returns
  /// <see cref="ChiselOutcome.NotChiseling"/> when the click isn't a chisel-out here (so the caller falls
  /// through to its other interactions), <see cref="ChiselOutcome.Blocked"/> when the player is chiselling
  /// content that isn't ready (the block error, if any, is sent), and <see cref="ChiselOutcome.Chiseled"/>
  /// once the hardened content has been chipped out, recovered into the player's inventory (or spawned at
  /// <paramref name="yOffset"/>), the chisel damaged (<paramref name="damageChisel"/>) and
  /// <paramref name="sound"/> played. All world mutation is server-side; the outcome is still returned on
  /// the client so the caller can claim the click for prediction.
  /// </summary>
  public static ChiselOutcome TryChisel(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos pos,
    IChiselableMolten target,
    AssetLocation sound,
    bool damageChisel = true,
    double yOffset = 0.6
  ) {
    if (!HasChiselAndHammer(byPlayer) || !target.HasChiselableContent)
      return ChiselOutcome.NotChiseling;

    if (!target.CanChiselOut) {
      if (world.Side == EnumAppSide.Server && target.ChiselBlockedError != null)
        (byPlayer as IServerPlayer)?.SendIngameError(target.ChiselBlockedError);
      return ChiselOutcome.Blocked;
    }

    if (world.Side == EnumAppSide.Server) {
      ItemStack? recovered = target.ChiselOut();
      if (
        recovered != null
        && !byPlayer.InventoryManager.TryGiveItemstack(recovered)
      )
        world.SpawnItemEntity(recovered, pos.ToVec3d().Add(0.5, yOffset, 0.5));

      ItemSlot? slot = byPlayer.InventoryManager.ActiveHotbarSlot;
      if (
        damageChisel
        && byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative
      )
        slot?.Itemstack?.Collectible.DamageItem(
          world,
          byPlayer.Entity,
          slot,
          2
        );

      ExSounds.Play(world.Api, pos, sound, 0.8f);
    }
    return ChiselOutcome.Chiseled;
  }

  // The chisel items advertised in the chisel-out interaction help, resolved once.
  private static ItemStack[]? _chiselStacks;

  /// <summary>The "chip out the solidified metal" interaction hint, advertising every chisel item.</summary>
  public static WorldInteraction ChiselHelp(
    IWorldAccessor world,
    string langCode
  ) =>
    new() {
      ActionLangCode = langCode,
      MouseButton = EnumMouseButton.Right,
      Itemstacks = _chiselStacks ??=
        [
          .. world
            .SearchItems(new AssetLocation("chisel-*"))
            .Select(i => new ItemStack(i)),
        ],
    };
}

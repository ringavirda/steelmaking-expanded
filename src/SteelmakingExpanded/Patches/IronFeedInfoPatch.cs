using System;
using System.Text;
using HarmonyLib;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using SteelmakingExpanded.Compat;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SteelmakingExpanded.Patches;

/// <summary>
/// Harmony patch that states an item's blast furnace worth on its tooltip, so the two feeds can be
/// compared against each other and against the bloomery route the same item already shows.
/// <para>
/// Hooked on the collectible rather than declared on the items, because the accepted feed includes
/// crushed ores contributed by other mods (see <see cref="IronOreCompat"/>) that no asset patch of
/// ours can reach. Vanilla's nugget item overrides this method but calls the base implementation,
/// so nuggets are covered too.
/// </para>
/// </summary>
[HarmonyPatch(typeof(Item), nameof(Item.GetHeldItemInfo))]
public static class IronFeedInfoPatch {
  [HarmonyPostfix]
  public static void AppendFurnaceYield(ItemSlot inSlot, StringBuilder dsc) {
    string? path = inSlot?.Itemstack?.Collectible?.Code?.Path;
    if (path == null)
      return;

    int oreUnits =
      IronOreCompat.IsCrushedIronOre(path) ? BurdenValue.OrePerCrushed
      : IronOreCompat.IsIronNugget(path) ? BurdenValue.OrePerNugget
      : 0;
    if (oreUnits <= 0)
      return;

    dsc.AppendLine(
      Lang.Get(
        "smex:itemdesc-blastfurnace-yield",
        Math.Round(BurdenValue.IronPerOreUnits(oreUnits), 1)
      )
    );
  }
}

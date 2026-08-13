using System.Collections.Generic;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Compat;

/// <summary>
/// Cross-mod compat for the blast furnace's iron-ore feed.
/// <para>To add support for a new mod: extend <see cref="Init"/> with an <c>IsModEnabled</c> branch that adds
/// the mod's paths to whichever set carries the rate that feed is worth -
/// <see cref="CrushedIronOrePaths"/> (<see cref="BlockStructures.BlastFurnace.BurdenValue.OrePerCrushed"/>),
/// <see cref="IronNuggetPaths"/> (<see cref="BlockStructures.BlastFurnace.BurdenValue.OrePerNugget"/>) or
/// <see cref="RoastedIronOrePaths"/> (the higher
/// <see cref="BlockStructures.BlastFurnace.BurdenValue.OrePerRoasted"/>). Each entry is the collectible's
/// <see cref="AssetLocation.Path"/> and is matched exactly, so a mod whose codes merely start with one of
/// ours cannot feed the furnace at full iron value.</para>
/// </summary>
public static class IronOreCompat {
  // Vanilla's crushed iron ore: what every vanilla iron ore and iron nugget pulverises into.
  private const string VanillaCrushedIron = "crushed-iron";

  // The vanilla nuggets that smelt to an iron bloom.
  private static readonly string[] VanillaIronNuggets = [
    "nugget-limonite",
    "nugget-hematite",
    "nugget-magnetite",
  ];

  // Rebuilt by Init. Seeded with the vanilla feed so the furnace still resolves ore fed to it by a
  // caller that runs before the mod system does. Vanilla roasts nothing, so that set starts empty.
  private static readonly HashSet<string> CrushedIronOrePaths = [VanillaCrushedIron];
  private static readonly HashSet<string> IronNuggetPaths = new(VanillaIronNuggets);
  private static readonly HashSet<string> RoastedIronOrePaths = [];

  /// <summary>
  /// Populate the compat lists.
  /// </summary>
  public static void Init(ICoreAPI api) {
    CrushedIronOrePaths.Clear();
    IronNuggetPaths.Clear();
    RoastedIronOrePaths.Clear();
    IronNuggetPaths.UnionWith(VanillaIronNuggets);

    if (api.ModLoader.IsModEnabled("industrialstory")) {
      CrushedIronOrePaths.Add("crushed-hematite");
      CrushedIronOrePaths.Add("crushed-magnetite");

      // Limonite and siderite are roasted rather than crushed there. Both forms the roaster returns
      // carry the premium: the extra heat step is the work being paid for, not the crushing.
      RoastedIronOrePaths.Add("roasted-nugget-iron");
      RoastedIronOrePaths.Add("roasted-crushed-iron");
    } else {
      // IndustrialStory reroutes every ore and nugget away from game:crushed-iron and then crushes
      // iron bits into it, which leaves the bits the furnace itself drops as its only source. One
      // bit costs MoltenUnitsPerBit but returns OrePerCrushed worth of burden, so accepting it there
      // would hand back more molten iron than it was made from.
      CrushedIronOrePaths.Add(VanillaCrushedIron);
    }

    // Expanded Matter per-ore crushed iron variants (all smelt to ironbloom).
    if (api.ModLoader.IsModEnabled("em")) {
      CrushedIronOrePaths.Add("crushed-ore-hematite");
      CrushedIronOrePaths.Add("crushed-ore-limonite");
      CrushedIronOrePaths.Add("crushed-ore-magnetite");
    }
  }

  /// <summary>
  /// True if <paramref name="path"/> is a recognised crushed-iron-ore item path for the blast furnace feed.
  /// </summary>
  public static bool IsCrushedIronOre(string path) =>
    CrushedIronOrePaths.Contains(path);

  /// <summary>
  /// True if <paramref name="path"/> is an iron nugget the blast furnace takes uncrushed, at the
  /// lower rate held by <see cref="BlockStructures.BlastFurnace.BurdenValue.OrePerNugget"/>.
  /// </summary>
  public static bool IsIronNugget(string path) => IronNuggetPaths.Contains(path);

  /// <summary>
  /// True if <paramref name="path"/> is roasted iron ore, which the furnace takes at the higher rate
  /// held by <see cref="BlockStructures.BlastFurnace.BurdenValue.OrePerRoasted"/>.
  /// </summary>
  public static bool IsRoastedIronOre(string path) =>
    RoastedIronOrePaths.Contains(path);

  /// <summary>True if <paramref name="path"/> is any iron feed the blast furnace burden accepts.</summary>
  public static bool IsIronFeed(string path) =>
    IsCrushedIronOre(path) || IsIronNugget(path) || IsRoastedIronOre(path);
}

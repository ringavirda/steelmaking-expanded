using System.Collections.Generic;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Compat;

/// <summary>
/// Cross-mod compat for the blast furnace's iron-ore feed.
/// <para>To add support for a new mod: extend <see cref="Init"/> with an <c>IsModEnabled</c> branch that adds the new paths to
/// <see cref="ExtraIronOrePaths"/>. Each entry is the collectible's <see cref="AssetLocation.Path"/>.</para>
/// </summary>
public static class IronOreCompat
{
  private static readonly HashSet<string> ExtraIronOrePaths = new();

  /// <summary>
  /// Populate the compat list.
  /// </summary>
  public static void Init(ICoreAPI api)
  {
    ExtraIronOrePaths.Clear();

    // IndustrialStory crushed iron ores.
    if (api.ModLoader.IsModEnabled("industrialstory"))
    {
      ExtraIronOrePaths.Add("crushed-hematite");
      ExtraIronOrePaths.Add("crushed-magnetite");
      ExtraIronOrePaths.Add("roasted-crushed-iron");
    }

    // Expanded Matter per-ore crushed iron variants (all smelt to ironbloom).
    if (api.ModLoader.IsModEnabled("em"))
    {
      ExtraIronOrePaths.Add("crushed-ore-hematite");
      ExtraIronOrePaths.Add("crushed-ore-limonite");
      ExtraIronOrePaths.Add("crushed-ore-magnetite");
    }
  }

  /// <summary>
  /// True if <paramref name="path"/> is a recognised crushed-iron-ore item path for the blast furnace feed.
  /// </summary>
  public static bool IsCrushedIronOre(string path) =>
    path.StartsWith("crushed-iron") || ExtraIronOrePaths.Contains(path);
}

using System.Collections.Generic;
using ExpandedLib.BlockMigrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace PipesAndPowerExpanded.BlockMigrations;

/// <summary>
/// Migrates the gas pipe network that moved out of Steelmaking Expanded (<c>smex</c>) into this
/// mod (<c>ppex</c>) and was renamed <c>gaspipe-* → pipe-*</c>. Several shapes also changed their
/// variant structure between versions, so a plain <c>gas</c>-prefix swap is not enough:
///
/// <list type="bullet">
/// <item><description>straight / bend / t-junction / x-junction and both valves gained an
/// iron/steel <c>material</c> axis the old pipes never had; each old (material-less) code maps to
/// the <c>iron</c> variant of the new block (steel is new and has no legacy equivalent).</description></item>
/// <item><description>outlet / passthrough / passthrough-bend keep their <c>brick</c> axis, but the
/// three <c>refractorytier1/2/3</c> bricks were dropped; old placements of those map to the
/// <c>fire</c> brick.</description></item>
/// <item><description>the old inline gas machines (<c>blower</c>, <c>heated</c>, <c>intake</c>) were
/// removed; their placements become a plain <c>iron</c> straight pipe of the matching axis so the
/// network stays continuous.</description></item>
/// </list>
/// </summary>
public class PipeMigration : IBlockCodeMigration
{
  public string Name => "Pipes and steam power moved to ppex";

  // Pipe shapes that gained an iron/steel material axis (old smex code had no material suffix).
  private static readonly HashSet<string> MaterialTypes =
  [
    "straight",
    "bend",
    "tjunction",
    "xjunction",
    "valve",
    "pressurevalve",
  ];

  // Old per-axis/per-facing orientations collapsed onto the surviving straight-pipe axes.
  private static readonly Dictionary<string, string> StraightAxis = new()
  {
    ["ns"] = "ns",
    ["sn"] = "ns",
    ["n"] = "ns",
    ["s"] = "ns",
    ["we"] = "we",
    ["ew"] = "we",
    ["w"] = "we",
    ["e"] = "we",
  };

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  )
  {
    foreach (Block block in api.World.Blocks)
    {
      if (block?.Code == null || block.Code.Domain != "ppex")
        continue;

      string path = block.Code.Path;
      string? type = block.Variant["type"];

      // straight/bend/t-/x-junction + valves: old code is the material-less gaspipe variant.
      // Only the iron variant has a legacy origin; steel pipes are new this version.
      if (type != null && MaterialTypes.Contains(type))
      {
        if (block.Variant["material"] != "iron")
          continue;
        string orient = block.Variant["orientation"];
        yield return (
          new AssetLocation("smex", $"gaspipe-{type}-{orient}"),
          block.Code.Clone()
        );
        continue;
      }

      // outlet/passthrough/passthrough-bend keep the brick axis: a straight gas-prefix swap of
      // the still-shared bricks (the dropped refractory tiers are handled separately below).
      if (path.StartsWith("pipe-"))
        yield return (
          new AssetLocation("smex", "gas" + path),
          block.Code.Clone()
        );
    }

    // Refractory bricks were removed from outlet/passthrough/passthrough-bend; fall back to fire.
    string[] refractory =
    [
      "refractorytier1",
      "refractorytier2",
      "refractorytier3",
    ];
    (string Type, string[] Orients)[] brickShapes =
    [
      ("outlet", ["s", "n", "w", "e"]),
      ("passthrough", ["ns", "we", "ud"]),
      (
        "passthroughbend",
        ["nw", "se", "en", "ws", "un", "us", "uw", "ue", "dn", "ds", "dw", "de"]
      ),
    ];
    foreach (var (shape, orients) in brickShapes)
    foreach (string tier in refractory)
    foreach (string orient in orients)
      yield return (
        new AssetLocation("smex", $"gaspipe-{shape}-{tier}-{orient}"),
        new AssetLocation("ppex", $"pipe-{shape}-fire-{orient}")
      );

    // Pre-brick legacy passthrough/outlet (before the brick variantgroup existed) → fire brick.
    foreach (string o in new[] { "ns", "we", "ud" })
      yield return (
        new AssetLocation("smex", $"gaspipe-passthrough-{o}"),
        new AssetLocation("ppex", $"pipe-passthrough-fire-{o}")
      );
    foreach (string o in new[] { "s", "n", "w", "e" })
      yield return (
        new AssetLocation("smex", $"gaspipe-outlet-{o}"),
        new AssetLocation("ppex", $"pipe-outlet-fire-{o}")
      );

    // Removed inline gas machines → a plain iron straight pipe of the matching axis.
    foreach (string o in new[] { "ns", "we" })
      yield return (
        new AssetLocation("smex", $"gaspipe-blower-{o}"),
        new AssetLocation("ppex", $"pipe-straight-{o}-iron")
      );

    string[] heatedBricks =
    [
      "fire",
      "black",
      "brown",
      "cream",
      "gray",
      "orange",
      "red",
      "tan",
      "refractorytier1",
      "refractorytier2",
      "refractorytier3",
    ];
    foreach (string brick in heatedBricks)
    foreach (string o in new[] { "ns", "sn", "we", "ew" })
      yield return (
        new AssetLocation("smex", $"gaspipe-heated-{brick}-{o}"),
        new AssetLocation("ppex", $"pipe-straight-{StraightAxis[o]}-iron")
      );

    foreach (string o in new[] { "n", "s", "w", "e" })
      yield return (
        new AssetLocation("smex", $"gaspipe-intake-{o}"),
        new AssetLocation("ppex", $"pipe-straight-{StraightAxis[o]}-iron")
      );
  }
}

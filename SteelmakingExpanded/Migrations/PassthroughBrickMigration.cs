using System.Collections.Generic;
using BlockMigrationLib;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.Migrations;

/// <summary>
/// Migrates pre-brick-variant gas passthroughs. The straight passthrough used the
/// brickless codes <c>smex:gaspipe-passthrough-{ns|we|ud}</c>; adding the brick
/// variantgroup changed those codes, so old placements load as missing-block
/// placeholders. Each is rewritten to the refractory-tier3 variant of the same
/// orientation (refractory brick was the original recipe ingredient).
/// </summary>
public class PassthroughBrickMigration : IBlockCodeMigration
{
  private static readonly string[] Orientations = ["ns", "we", "ud"];

  public string Name => "Gas passthrough brick variants";

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  )
  {
    foreach (string orient in Orientations)
      yield return (
        new AssetLocation("smex", $"gaspipe-passthrough-{orient}"),
        new AssetLocation(
          "smex",
          $"gaspipe-passthrough-refractorytier3-{orient}"
        )
      );
  }
}

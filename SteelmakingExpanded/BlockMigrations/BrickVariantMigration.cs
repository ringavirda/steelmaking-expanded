using System.Collections.Generic;
using ExpandedLib.BlockMigrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Migrates blocks that gained a brick/refractory-tier variantgroup. The cowper-stove and
/// smoke-stack intakes originally used codes without that group (e.g.
/// <c>smex:cowperstove-intake-s</c>); adding the group changed those codes, so old placements
/// load as missing-block placeholders. Each is rewritten to the tier3 variant of the same base
/// and orientation. (The pipe passthrough/outlet that also gained the group have since moved to
/// the ppex mod; their migration lives in <c>PipeMigration</c> there.)
/// </summary>
public class BrickVariantMigration : IBlockCodeMigration
{
  /// <summary>
  /// One block that gained a variant: its code without the new group, the variant value
  /// inserted before the orientation, and the orientations that existed beforehand.
  /// </summary>
  private readonly record struct Entry(
    string CodeBase,
    string InsertedVariant,
    string[] Orientations
  );

  private static readonly Entry[] Entries =
  [
    new("cowperstove-intake", "tier3", ["n", "s", "w", "e"]),
    new("smokestack-intake", "tier3", ["n", "s", "w", "e"]),
  ];

  public string Name => "Brick and refractory-tier variants";

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  )
  {
    foreach (var (codeBase, inserted, orientations) in Entries)
    foreach (string orient in orientations)
      yield return (
        new AssetLocation("smex", $"{codeBase}-{orient}"),
        new AssetLocation("smex", $"{codeBase}-{inserted}-{orient}")
      );
  }
}

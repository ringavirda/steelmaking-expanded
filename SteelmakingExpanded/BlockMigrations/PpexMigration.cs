using System.Collections.Generic;
using System.Linq;
using ExpandedLib.BlockMigrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

public class PpexMigration : IBlockCodeMigration
{
  private readonly record struct Entry(
    string CodeBase,
    string[] Variants,
    string[] OldOrientations,
    string[] NewOrientations
  );

  private static readonly Entry[] Entries =
  [
    new(
      "cowperstove-intake",
      ["tier1", "tier2", "tier3"],
      ["n", "s", "w", "e"],
      ["north", "south", "west", "east"]
    ),
  ];

  public string Name => "Cowper stove intake not network node.";

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  )
  {
    foreach (
      var (codeBase, variations, oldOrientations, newOrientations) in Entries
    )
    foreach (string variant in variations)
    foreach (var (i, oldOrient) in oldOrientations.Index())
      yield return (
        new AssetLocation("smex", $"{codeBase}-{variant}-{oldOrient}"),
        new AssetLocation("smex", $"{codeBase}-{variant}-{newOrientations[i]}")
      );
  }
}

using System.Collections.Generic;
using ExpandedLib.Blocks.Migrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Migrates the blast furnace parts that gained a <c>refractory</c> tier variant - the door, the
/// tuyeres and the molten metal taps. Each drew its brick from the tier3 texture whatever the furnace
/// was built from, so every existing placement is remapped to the tier3 variant: the one it already
/// looked like.
/// </summary>
public class BlastFurnaceTierMigration : IBlockCodeMigration {
  public string Name => "Blast furnace refractory tiers";

  private static readonly string[] Sides = ["north", "south", "east", "west"];
  private static readonly string[] Orientations = ["n", "s", "w", "e"];

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  ) {
    // The door carried no variant at all.
    yield return (
      new AssetLocation("smex", "blastfurnacedoor"),
      new AssetLocation("smex", "blastfurnacedoor-tier3")
    );

    // The tap keeps its side, with the tier inserted ahead of it.
    foreach (string side in Sides)
      yield return (
        new AssetLocation("smex", $"blastfurnacetap-{side}"),
        new AssetLocation("smex", $"blastfurnacetap-tier3-{side}")
      );

    // The tuyere takes the tier between its type and its orientation.
    foreach (string orient in Orientations)
      yield return (
        new AssetLocation("smex", $"blastfurnace-tuyere-{orient}"),
        new AssetLocation("smex", $"blastfurnace-tuyere-tier3-{orient}")
      );
  }
}

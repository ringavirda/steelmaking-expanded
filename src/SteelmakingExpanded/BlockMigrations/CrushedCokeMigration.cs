using System.Collections.Generic;
using ExpandedLib.Blocks.Migrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Converts leftover <c>game:crushed-coke</c> back to <c>game:coke</c>.
/// <para>
/// Crushed coke existed only as the blast furnace's fuel intermediate, and producing it meant
/// bolting a <c>crushingProps</c> entry onto vanilla coke - which put smex in the middle of every
/// other mod's crushing economy. The furnace now takes coke whole, so the intermediate is retired
/// and the pulverizer route with it.
/// </para>
/// <para>
/// The swap is one-for-one rather than the old 1:2 crush ratio: it hands a player slightly more
/// fuel value than they had, which is the safe direction for a migration nobody opted into. The
/// per-batch coke cost is restated to match (3 crushed = 1.5 coke, rounded to 2), so the chain
/// costs about what it did.
/// </para>
/// </summary>
public class CrushedCokeMigration : IItemCodeMigration {
  public string Name => "Crushed coke to coke";

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  ) {
    yield return (
      new AssetLocation("game", "crushed-coke"),
      new AssetLocation("game", "coke")
    );
  }
}

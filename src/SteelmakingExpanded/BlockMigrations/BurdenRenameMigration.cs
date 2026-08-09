using System.Collections.Generic;
using ExpandedLib.Blocks.Migrations;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Renames <c>smex:blastmix</c> to <c>smex:burden</c>. "Burden" is what the charge of ore, fuel and
/// flux a blast furnace is loaded with is actually called, and it is the term the Russian and
/// Ukrainian translations already used.
/// </summary>
public class BurdenRenameMigration : IItemCodeMigration {
  public string Name => "Blast mix to burden";

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  ) {
    yield return (
      new AssetLocation("smex", "blastmix"),
      new AssetLocation("smex", "burden")
    );
  }
}

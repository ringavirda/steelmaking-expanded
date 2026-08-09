using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks.Migrations;
using SteelmakingExpanded.Molds;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Purges any tool mold whose type a server admin has disabled (<c>/exmod molds ... off</c>) from the
/// world and from inventories on the next reload - the existing-content half of the disable, run
/// through the shared <see cref="BlockMigrationModSystem"/> sweep. Reads the live config via
/// <see cref="MoldGating.IsToolMoldDisabled"/>, so it is a no-op while every mold is enabled. Molds
/// sitting in a mold pedestal are stored outside any inventory, so the pedestal clears those itself.
/// </summary>
public class MoldRemoval : IBlockRemoval {
  public string Name => "Disabled tool molds";

  public IEnumerable<AssetLocation> GetRemovals(ICoreServerAPI api) =>
    api
      .World.Blocks.Where(b => MoldGating.IsToolMoldDisabled(b?.Code))
      .Select(b => b.Code);
}

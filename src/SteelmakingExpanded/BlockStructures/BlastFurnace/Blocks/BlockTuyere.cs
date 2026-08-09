using System.Collections.Generic;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>
/// Tuyere: a single-faced gas-pipe node built into the blast furnace through which
/// air or hot blast is drawn into the hearth.
/// </summary>
[BlockRegister]
public partial class BlockTuyere : BlockPipe {
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "tuyere", ["s", "n", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) => "s";
}

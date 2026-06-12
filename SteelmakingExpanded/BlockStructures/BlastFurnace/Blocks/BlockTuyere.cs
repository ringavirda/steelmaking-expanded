using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>
/// Tuyere: a single-faced gas-pipe node built into the blast furnace through which
/// air or hot blast is drawn into the hearth.
/// </summary>
[EntityRegister]
public class BlockTuyere : BlockPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "tuyere", ["s", "n", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) => "s";
}

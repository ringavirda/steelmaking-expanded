using System.Collections.Generic;
using SteelmakingExpanded.Networks.Gas.Blocks;

namespace SteelmakingExpanded.Structures.BlastFurnace.Blocks;

/// <summary>
/// Tuyere: a single-faced gas-pipe node built into the blast furnace through which
/// air or hot blast is drawn into the hearth.
/// </summary>
public class BlockTuyere : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "tuyere", ["s", "n", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) => "s";
}

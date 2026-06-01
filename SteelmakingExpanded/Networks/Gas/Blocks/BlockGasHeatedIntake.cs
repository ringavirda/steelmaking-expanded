using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Heated intake pipe: a network endpoint that transfers gas from one side to the
/// other and can heat or convert it to exhaust when a burning coal pile sits below.
/// </summary>
public class BlockGasHeatedIntake : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "heated", ["ns", "sn", "we", "ew"] } };

  public override bool IsNetworkEndPoint => true;

  protected override string GetFallbackOrientation(string? type) => "ns";
}

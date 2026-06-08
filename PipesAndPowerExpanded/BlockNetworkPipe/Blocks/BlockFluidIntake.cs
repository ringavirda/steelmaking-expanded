using System.Collections.Generic;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

[EntityRegister]
public class BlockFluidIntake : BlockNetworkNode
{
  public override string NetworkType => "pipe";

  public override Dictionary<string, string[]> AllowedOrientations =>
    new() { { "fluidintake", ["s", "n", "e", "w"] } };

  protected override string GetFallbackOrientation(string? type) => "s";
}

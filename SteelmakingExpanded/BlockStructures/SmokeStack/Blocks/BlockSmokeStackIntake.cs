using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.BlockStructures.SmokeStack.Blocks;

/// <summary>
/// Intake/anchor block of the smoke-stack multiblock. Vents surplus exhaust gas from
/// the network to the sky. The build-outline projection (Ctrl + Shift + right-click) is
/// provided by the shared <c>MultiblockStructure</c> block behavior declared in the
/// block JSON.
/// </summary>
[EntityRegister]
public class BlockSmokeStackIntake : BlockPipePassthrough
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "intake", ["n", "s", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) => "n";
}

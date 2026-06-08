using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Structures.Metalworking.CowperStove.Blocks;

/// <summary>
/// Intake/anchor block of the cowper-stove multiblock. Routes exhaust gas into the
/// stove. The build-outline projection (Ctrl + Shift + right-click) is provided by the
/// shared <c>MultiblockStructure</c> block behavior declared in the block JSON.
/// </summary>
[EntityRegister]
public class BlockCowperStoveIntake : BlockPipePassthrough
{
  public override Dictionary<string, string[]> AllowedOrientations =>
    new() { { "intake", ["n", "s", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) => "n";
}

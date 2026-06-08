using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Gas outlet: a single-faced pipe that connects the gas network to a structure's
/// face (e.g. a furnace or cowper-stove port), used to inject or extract gas there.
/// </summary>
[EntityRegister]
public class BlockPipeOutlet : BlockPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "outlet", ["s", "n", "e", "w", "u", "d"] } };

  public override bool HasConnectorAt(BlockFacing face) =>
    Orientation != null && Orientation.EndsWith(face.Code[0]);

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace) || SideSolid[blockFace.Index];

  protected override string GetFallbackOrientation(string? type) => "s";
}

using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Gas outlet: a single-faced pipe that connects the gas network to a structure's
/// face (e.g. a furnace or cowper-stove port), used to inject or extract gas there.
/// </summary>
public class BlockGasOutlet : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "outlet", ["s", "n", "e", "w"] } };

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

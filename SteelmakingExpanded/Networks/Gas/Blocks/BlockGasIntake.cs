using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Air intake: a single-faced pipe that draws fresh air into the gas network. Its
/// one connector is on the trailing face, so it can be mounted flush against a wall.
/// </summary>
public class BlockGasIntake : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "intake", ["n", "s", "w", "e"] } };

  public override bool HasConnectorAt(BlockFacing face) =>
    Orientation != null && Orientation.EndsWith(face.Code[0]);

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace) || SideSolid[blockFace.Index];

  protected override string GetFallbackOrientation(string? type) => "n";
}

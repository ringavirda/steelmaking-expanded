using System.Collections.Generic;
using SteelmakingExpanded.Networks.Gas.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures.BessemerConverter.Blocks;

/// <summary>Single-faced gas-pipe node built into the converter that feeds blast into the refining vessel.</summary>
public class BlockBessemerGasIntake : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "gasintake", ["n", "s", "w", "e"] } };

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

using System.Collections.Generic;
using SteelmakingExpanded.Structures.CowperStove.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Passthrough pipe: carries gas straight through a wall and seals against a
/// <see cref="BlockHeatSink"/> neighbour without registering it as a leak.
/// </summary>
public class BlockGasPassthrough : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "passthrough", ["ns", "we", "ud"] } };

  protected override string GetFallbackOrientation(string? type) => "ns";

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => SideSolid[blockFace.Index] || HasConnectorAt(blockFace);

  public override bool IsValidNonNetworkConnection(
    Block neighborBlock,
    BlockFacing face
  ) => neighborBlock is BlockHeatSink;

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  ) { }
}

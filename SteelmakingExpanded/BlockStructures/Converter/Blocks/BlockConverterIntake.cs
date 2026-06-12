using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.Converter.Blocks;

/// <summary>
/// The converter's blast intake: a fixed structure port (not a network node) that
/// exposes a single pipe connector on the face it is turned to face. A pipe run
/// docks against that connector and the <see cref="BlockEntities.BlockEntityConverterControl"/>
/// reads/consumes blast from the network on the other side of it. Horizontally
/// orientable so it can be aligned with the control block.
/// </summary>
[EntityRegister]
public class BlockConverterIntake : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  /// <summary>
  /// The single horizontal face that carries the pipe connector, derived from the
  /// block's <c>side</c> variant (north → north, rotated for the other sides).
  /// </summary>
  public BlockFacing ConnectorFace =>
    ExOrientation.RotateFacing(
      BlockFacing.NORTH,
      ExOrientation.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) => face == ConnectorFace;

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace) || SideSolid[blockFace.Index];
}

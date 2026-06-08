using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// The steam condenser: a tjunction-shaped fixed port (not a network node) that
/// recycles leftover steam back into hot water. In its default (north) orientation it
/// exposes three horizontal pipe connectors — west and east are the steam line / hot-
/// water output (whichever side carries steam feeds the condenser; the other receives
/// the hot water), and north is the coolant-water line. The three adjacent pipe runs
/// stay separate networks because the condenser is a connector, not a node.
/// </summary>
[EntityRegister]
public class BlockSteamCondenser : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  private int Angle => StructureFillers.AngleFromSide(Variant["side"]);

  /// <summary>One of the two interchangeable horizontal steam/output cells (local west).</summary>
  public BlockPos SideAPos(BlockPos pos) =>
    pos.AddCopy(StructureFillers.RotateFacing(BlockFacing.WEST, Angle));

  /// <summary>The other interchangeable horizontal steam/output cell (local east).</summary>
  public BlockPos SideBPos(BlockPos pos) =>
    pos.AddCopy(StructureFillers.RotateFacing(BlockFacing.EAST, Angle));

  /// <summary>The coolant-water cell (local north).</summary>
  public BlockPos CoolantPos(BlockPos pos) =>
    pos.AddCopy(StructureFillers.RotateFacing(BlockFacing.NORTH, Angle));

  public bool HasConnectorAt(BlockFacing face)
  {
    int angle = Angle;
    return face == StructureFillers.RotateFacing(BlockFacing.WEST, angle)
      || face == StructureFillers.RotateFacing(BlockFacing.EAST, angle)
      || face == StructureFillers.RotateFacing(BlockFacing.NORTH, angle);
  }

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace) || SideSolid[blockFace.Index];
}

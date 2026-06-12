using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// The steam condenser: a tjunction-shaped fixed port (not a network node) that sits in
/// the main water line and recycles leftover steam back into it. In its default (north)
/// orientation it exposes three horizontal pipe connectors — <b>west and east are the
/// water line</b> (water flows through, carrying the condensate downstream) and <b>north
/// is the steam line</b>. Steam drawn from the north condenses into the water passing
/// W↔E, warming and merging with it. The three adjacent pipe runs stay separate networks
/// because the condenser is a connector, not a node — the block entity bridges the two
/// water sides itself.
/// </summary>
[EntityRegister]
public class BlockSteamCondenser : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  private int Angle => ExOrientation.AngleFromSide(Variant["side"]);

  // JSON collision/selection boxes are authored in the north orientation and do not
  // auto-rotate with the placed variant, so rotate them to match the shape's rotateY.
  private Cuboidf[]? _rotatedCollisionBoxes;
  private Cuboidf[]? _rotatedSelectionBoxes;

  public override Cuboidf[] GetCollisionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    _rotatedCollisionBoxes ??= ExOrientation.RotateBoxes(CollisionBoxes, Angle);

  public override Cuboidf[] GetSelectionBoxes(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) =>
    _rotatedSelectionBoxes ??= ExOrientation.RotateBoxes(SelectionBoxes, Angle);

  /// <summary>Facing of one of the two horizontal water connectors (local west).</summary>
  public BlockFacing SideAFace =>
    ExOrientation.RotateFacing(BlockFacing.WEST, Angle);

  /// <summary>Facing of the other horizontal water connector (local east).</summary>
  public BlockFacing SideBFace =>
    ExOrientation.RotateFacing(BlockFacing.EAST, Angle);

  /// <summary>Facing of the steam-inlet connector (local north).</summary>
  public BlockFacing SteamInletFace =>
    ExOrientation.RotateFacing(BlockFacing.NORTH, Angle);

  public bool HasConnectorAt(BlockFacing face)
  {
    int angle = Angle;
    return face == ExOrientation.RotateFacing(BlockFacing.WEST, angle)
      || face == ExOrientation.RotateFacing(BlockFacing.EAST, angle)
      || face == ExOrientation.RotateFacing(BlockFacing.NORTH, angle);
  }

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace) || SideSolid[blockFace.Index];
}

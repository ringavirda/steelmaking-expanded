using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockStructures.MpPump.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.BlockStructures.MpPump.Blocks;

/// <summary>
/// The mechanical (walking-beam) fluid pump: an L-shaped mega-block driven from the mechanical
/// power network rather than by steam, so a water loop can be raised before the boiler it feeds is
/// lit. The principal cell is the near, low corner and carries the axle coupling; both pipe ports
/// sit on fillers at the far end, where the shape draws its valve column - the source enters the
/// underside of the low cell and the delivery leaves the far face of the high one. All work lives
/// in <see cref="BlockEntityMpFluidPump"/>.
/// </summary>
/// <remarks>
/// Footprint in the north orientation, Y-Z slice (rows run -Y, columns run +Z):
/// <code>
/// .  .  P     P = delivery port filler, south face
/// O  #  S     S = source port filler, down face
/// </code>
/// The body extends south of the principal, so the shape is drawn rotated a further 180°.
/// </remarks>
[BlockRegister]
public partial class BlockMpFluidPump
  : Block,
    IMechanicalPowerBlock,
    IFillerHost {
  /// <summary>Horizontal placement angle (north 0, west 90, south 180, east 270).</summary>
  public int Angle => ExOrientation.AngleFromSide(Variant["side"]);

  /// <summary>
  /// The delivery face at the far, high end of the housing - south in the north orientation, that
  /// is, pointing away from the principal along the body.
  /// </summary>
  public BlockFacing OutputFace =>
    ExOrientation.RotateFacing(BlockFacing.SOUTH, Angle);

  /// <summary>
  /// The source face: the underside of the far, low cell, where the shape opens the bottom of the
  /// valve column. Vertical, so it does not rotate with the block.
  /// </summary>
  public static BlockFacing SourceFace => BlockFacing.DOWN;

  /// <summary>The face the drive axle couples on - east in the north orientation.</summary>
  public BlockFacing DriveFace =>
    ExOrientation.RotateFacing(BlockFacing.EAST, Angle);

  private BlockPos OffsetWorldPos(
    BlockPos pumpPos,
    JsonObject? offsetNode,
    Vec3i fallback
  ) => ExOrientation.WorldPosFromAttr(pumpPos, offsetNode, fallback, Angle);

  /// <summary>World cell of the filler carrying the source port; the intake line attaches below it.</summary>
  public BlockPos SourceWorldPos(BlockPos pumpPos) =>
    OffsetWorldPos(pumpPos, SourceOffset, new Vec3i(0, 0, 2));

  /// <summary>
  /// World cell of the filler carrying the delivery port; the delivery main attaches in the cell
  /// beyond it, across <see cref="OutputFace"/>.
  /// </summary>
  public BlockPos OutletWorldPos(BlockPos pumpPos) =>
    OffsetWorldPos(pumpPos, OutletOffset, new Vec3i(0, 1, 2));

  #region Mechanical power

  /// <summary>Accepts an axle on the drive face or its opposite - both ends of the same shaft line.</summary>
  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
#if GAME_GE_1_22
    ,
    BlockMPBase forBlock
#endif
  ) => face == DriveFace || face == DriveFace.Opposite;

  public void DidConnectAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
  ) { }

  public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
    world
      .BlockAccessor.GetBlockEntity(pos)
      ?.GetBehavior<BEBehaviorMpPumpDrive>()
      ?.Network;

  #endregion

  #region Filler footprint

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    var cells = StructureFillers.FootprintCells(this, blockSel.Position, Angle);
    if (!StructureFillers.CanPlace(world, cells)) {
      failureCode = "notenoughspace";
      return false;
    }
    return true;
  }

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  ) {
    base.OnBlockPlaced(world, blockPos, byItemStack);
    StructureFillers.PlaceFillers(
      world,
      blockPos,
      StructureFillers.FootprintCells(this, blockPos, Angle)
    );
    MarkPort(world, SourceWorldPos(blockPos), SourceFace);
    MarkPort(world, OutletWorldPos(blockPos), OutputFace);
  }

  /// <summary>
  /// Turns a filler cell into a "pipe" port on <paramref name="face"/>, so a line laid against
  /// that face connects straight into the pump. Fillers can never be graph nodes themselves, so
  /// this fixed-port marker is how a mega-block presents a connector away from its own cell.
  /// </summary>
  private static void MarkPort(
    IWorldAccessor world,
    BlockPos cell,
    BlockFacing face
  ) {
    if (world.Side != EnumAppSide.Server)
      return;
    if (
      world.BlockAccessor.GetBlockEntity(cell) is BlockEntityStructureFiller be
    ) {
      be.PortFace = face.Code[0].ToString();
      be.PortNetworkType = "pipe";
      be.MarkDirty(true);
    }
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells(this, pos, Angle)
    );
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  #endregion
}

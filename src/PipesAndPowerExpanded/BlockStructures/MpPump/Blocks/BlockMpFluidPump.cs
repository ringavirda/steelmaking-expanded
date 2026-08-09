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
/// Footprint in the structure frame, Y-Z slice (rows run -Y, columns run +Z):
/// <code>
/// .  .  P     P = delivery port filler, local south face
/// O  #  S     S = source port filler, down face
/// </code>
/// The principal carries the axle coupling on its local east face.
/// </remarks>
[BlockRegister]
public partial class BlockMpFluidPump
  : Block,
    IMechanicalPowerBlock,
    IFillerHost {
  /// <summary>Horizontal placement angle (north 0, west 90, south 180, east 270).</summary>
  public int Angle => ExOrientation.AngleFromSide(Variant["side"]);

  /// <summary>
  /// The rotation the footprint and the ports share, offset 180° from <see cref="Angle"/> so the
  /// housing is raised AWAY from the player - the same convention as <c>BlockBoiler</c>. Every
  /// offset and facing must go through this, never <see cref="Angle"/>: mixing the two puts the
  /// fillers where the player is standing and turns every connector to face the wrong way.
  /// <para>
  /// The shape does NOT take this angle. Its art is drawn along local -z while <c>fillerOffsets</c>
  /// are authored along local +z, so <c>shapebytype</c>'s rotateY is the plain placement angle and
  /// the two meet in the middle. Rotating both by the same amount points the model and the volume
  /// in opposite directions.
  /// </para>
  /// </summary>
  public int StructureAngle => (Angle + 180) % 360;

  /// <summary>
  /// The delivery face of the high cell - local north, looking back along the body into the notch
  /// of the L, which is where the shape opens the valve column. Not the far face: the cell beyond
  /// the far end is outside the structure, while the notch cell is the one a delivery main can
  /// actually be run into.
  /// </summary>
  public BlockFacing OutputFace =>
    ExOrientation.RotateFacing(BlockFacing.NORTH, StructureAngle);

  /// <summary>
  /// The source face: the underside of the far, low cell, where the shape opens the bottom of the
  /// valve column. Vertical, so it does not rotate with the block.
  /// </summary>
  public static BlockFacing SourceFace => BlockFacing.DOWN;

  /// <summary>
  /// The face the drive axle couples on - local east, where the shape draws the gear. This is the
  /// single source for the coupling side: <c>BEBehaviorMpPumpDrive</c> seeds its network-discovery
  /// facing from it, so the face the axle is accepted on and the face the network is discovered
  /// through can never drift apart.
  /// </summary>
  public BlockFacing DriveFace =>
    ExOrientation.RotateFacing(BlockFacing.EAST, StructureAngle);

  private BlockPos OffsetWorldPos(
    BlockPos pumpPos,
    JsonObject? offsetNode,
    Vec3i fallback
  ) =>
    ExOrientation.WorldPosFromAttr(
      pumpPos,
      offsetNode,
      fallback,
      StructureAngle
    );

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

  /// <summary>
  /// Accepts an axle on the drive face and nowhere else. The pump is an end of a line, not a length
  /// of shafting: it takes power in at the gear and does not pass it out the far side.
  /// </summary>
  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
#if GAME_GE_1_22
    ,
    BlockMPBase forBlock
#endif
  ) => face == DriveFace;

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

    var cells = StructureFillers.FootprintCells(
      this,
      blockSel.Position,
      StructureAngle
    );
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
      StructureFillers.FootprintCells(this, blockPos, StructureAngle)
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
      StructureFillers.FootprintCells(this, pos, StructureAngle)
    );
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  #endregion
}

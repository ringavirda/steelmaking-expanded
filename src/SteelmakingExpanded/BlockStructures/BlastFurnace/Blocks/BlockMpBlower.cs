using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>
/// The twin-tub mechanical blower: a 1 x 2 x 3 mega-block driven by an axle rather than by steam,
/// so a blast furnace can be blown from a waterwheel or windmill before any boiler exists. The
/// principal cell is the near, low corner; the rest of the volume is reserved with invisible
/// fillers, one of which hosts the mechanical-power port the axle couples to and one of which is
/// the pipe port the blast main butts against. All work lives in
/// <see cref="BlockEntities.BlockEntityMpBlower"/>.
/// </summary>
/// <remarks>
/// Footprint in the north orientation, Y-Z slice (rows run -Y, columns run +Z):
/// <code>
/// M  #  #     M = mechanical-power port filler, east face
/// O  #  B     B = blast outlet port filler, south face
/// </code>
/// The body extends south of the principal, so the shape is drawn rotated a further 180°.
/// </remarks>
[BlockRegister]
public partial class BlockMpBlower : Block, IFillerHost {
  /// <summary>Horizontal placement angle (north 0, west 90, south 180, east 270).</summary>
  public int Angle => ExOrientation.AngleFromSide(Variant["side"]);

  /// <summary>
  /// The face the blast main leaves on - south in the north orientation, that is, the far end of
  /// the housing where the shape draws its pipe stub.
  /// </summary>
  public BlockFacing OutletFace =>
    ExOrientation.RotateFacing(BlockFacing.SOUTH, Angle);

  private BlockPos OffsetWorldPos(
    BlockPos blowerPos,
    JsonObject? offsetNode,
    Vec3i fallback
  ) => ExOrientation.WorldPosFromAttr(blowerPos, offsetNode, fallback, Angle);

  /// <summary>World cell of the filler that hosts the mechanical-power port.</summary>
  public BlockPos MpPortWorldPos(BlockPos blowerPos) =>
    OffsetWorldPos(blowerPos, MpPortOffset, new Vec3i(0, 1, 0));

  /// <summary>
  /// World cell of the filler carrying the blast outlet; the pipe attaches in the cell beyond it,
  /// across <see cref="OutletFace"/>.
  /// </summary>
  public BlockPos BlastOutletWorldPos(BlockPos blowerPos) =>
    OffsetWorldPos(blowerPos, BlastOutletOffset, new Vec3i(0, 0, 2));

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // Refuse placement unless the whole volume is clear: without the fillers there is no port cell
    // for the axle and no outlet cell for the main, so the blower would stand inert.
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
    MarkBlastPort(world, blockPos);
  }

  /// <summary>
  /// Turns the outlet filler cell into a "pipe" port facing <see cref="OutletFace"/>, so a blast
  /// main laid against the far end of the housing connects straight into the blower.
  /// </summary>
  private void MarkBlastPort(IWorldAccessor world, BlockPos blowerPos) {
    if (world.Side != EnumAppSide.Server)
      return;
    if (
      world.BlockAccessor.GetBlockEntity(BlastOutletWorldPos(blowerPos))
      is BlockEntityStructureFiller be
    ) {
      be.PortFace = OutletFace.Code[0].ToString();
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
    // Clear the reserved volume first so no invisible solid cells are left behind.
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells(this, pos, Angle)
    );
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }
}

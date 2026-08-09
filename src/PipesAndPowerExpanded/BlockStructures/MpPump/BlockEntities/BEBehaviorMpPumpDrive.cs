using System;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.BlockStructures.MpPump.BlockEntities;

/// <summary>
/// Mechanical-power node for the mechanical fluid pump. In its natural (north) orientation the
/// axle couples on the west face, where the shape draws the gear; the connector rotates with the
/// block's "side" variant. The pump block entity reads the resulting network speed to size its
/// stroke - this behaviour only joins the network and loads it.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMpPumpDrive(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity) {
  /// <summary>
  /// Load the walking beam puts on the shaft: its own friction plus the torque the plunger draws to
  /// lift the delivery head. A displacement pump's torque demand is set by the column it raises, not
  /// by how fast it is turned, so this is a constant for a fixed head - but it is the term that
  /// makes drive size decide the pumping rate, since the network settles where the drive's torque
  /// meets it.
  /// </summary>
  public static float PumpResistance =>
    PpexValues.MpPumpBaseLoad
    + PpexValues.MpPumpLoadPerAtm * PpexValues.MpPumpDeliveryPressure;

  /// <summary>The network's rotation speed, absolute; 0 when no axle is coupled.</summary>
  public float DriveSpeed => Network != null ? Math.Abs(Network.Speed) : 0f;

  /// <summary>
  /// The axle's render angle (radians), for phase-locking the pump's cycle animation to the shaft
  /// via <see cref="MPAnim"/>; 0 when no axle is coupled. This already carries the visible turn
  /// direction, so it must not be combined with <see cref="BEBehaviorMPBase.AxisSign"/> on top.
  /// </summary>
  public float CurrentAngleRad => Network != null ? AngleRad : 0f;

  /// <summary>True while the axle is turning, that is, while the beam is being worked.</summary>
  public bool IsTurning => Network is { Speed: > 0.001f or < -0.001f };

  public override float GetResistance() => PumpResistance;

  public override void SetOrientations() {
    // Taken from the block, which is also what HasMechPowerConnectorAt answers with. Deriving it
    // here independently let the accepted face and the discovered face disagree, and an axle laid
    // against the gear was refused by one or ignored by the other.
    OutFacingForNetworkDiscovery =
      (Block as Blocks.BlockMpFluidPump)?.DriveFace ?? BlockFacing.WEST;

    // One sign per axis, not per facing: opposite facings share an axle line and must not
    // counter-rotate.
    AxisSign =
      OutFacingForNetworkDiscovery.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  /// <summary>
  /// A path no element matches, so the tesselator yields an empty mesh. A null or empty
  /// SelectiveElements means "no filter" and would draw the whole machine instead, so "draw
  /// nothing" has to be expressed as a selection of nothing.
  /// </summary>
  private static readonly string[] DrawsNothing = ["Root/-/*"];

  /// <summary>
  /// Keeps the mechanical-power renderer from drawing anything at all. The base seeds <c>Shape</c>
  /// from <c>Block.Shape</c> - the whole machine - and registers the device with the power
  /// renderer, which draws it independently of <see cref="OnTesselation"/>, so the finished pump
  /// appeared the moment it was placed and no OnTesselation override could suppress it.
  /// <para>
  /// Restricting it to the axle subtree was not enough either: the shape's <c>cycle</c> animation
  /// already turns <c>Axle</c>, and <c>Gear</c> and <c>Rod</c> ride it as children, so the power
  /// renderer's spin ran on top of the animation's and the shaft carried the wooden disc and the
  /// connecting rod round with it. The animator owns every moving part; this behaviour only joins
  /// the network and reports its angle, which the block entity locks the cycle to.
  /// </para>
  /// </summary>
  protected override CompositeShape GetShape() =>
    new() {
      Base = Block.Shape.Base.Clone(),
      SelectiveElements = DrawsNothing,
      rotateY = Block.Shape.rotateY,
      InsertBakedTextures = true,
    };

  /// <summary>The pump block entity draws the beam and gear, so the behaviour adds no mesh.</summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) => false;
}

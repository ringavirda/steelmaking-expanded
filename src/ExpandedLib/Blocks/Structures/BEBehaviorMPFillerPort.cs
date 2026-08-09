using System;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// A minimal mechanical-power node a mega-block hosts on one of its invisible footprint cells (see
/// <see cref="StructureFillers"/> / <see cref="IFillerHostedBehavior"/>), giving the MP network a
/// participant at the cell where an axle couples - the principal block, cells away, cannot accept
/// power at that face. The port renders nothing and only loads the network with a configurable
/// resistance; the principal reads back the resulting <see cref="BEBehaviorMPBase.Network"/> speed
/// and angle to drive its parts in sync. Orientation comes from the principal:
/// <see cref="ConfigureFromFiller"/> passes the connector face already rotated into the placed
/// orientation, not the shared filler block's own always-north variant.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorMPFillerPort(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity),
    IFillerHostedBehavior {
  /// <summary>Default network load when the declaration sets no <c>resistance</c> property.</summary>
  public const float DefaultResistance = 0.5f;

  private BlockFacing _face = BlockFacing.NORTH;
  private float _resistance = DefaultResistance;

  /// <summary>The face this port couples an axle on (already in the placed orientation).</summary>
  public BlockFacing PortFacing => _face;

  /// <summary>
  /// The network's current rotation angle (radians), for phase-locking a driven part to the axle
  /// via <see cref="ExpandedLib.Helpers.MPAnim"/>; 0 when the port has no network.
  /// </summary>
  public float CurrentAngleRad => Network != null ? AngleRad : 0f;

  /// <summary>True while the axle is turning, that is, while the port delivers power.</summary>
  public bool IsTurning => Network is { Speed: > 0.001f or < -0.001f };

  /// <summary>
  /// The network's rotation speed, absolute; 0 when the port has no network. A principal scales the
  /// work it does by it.
  /// </summary>
  public float Speed => Network != null ? Math.Abs(Network.Speed) : 0f;

  /// <summary>
  /// Which way the axle turns: true when the vanilla network runs negative. <see cref="Speed"/> is
  /// absolute, so a machine whose geometry depends on rotation direction reads the sign here.
  /// </summary>
  public bool IsReversed => Network is { Speed: < -0.001f };

  public void ConfigureFromFiller(
    BlockPos? principal,
    BlockFacing? connectorFace,
    JsonObject? properties
  ) {
    if (connectorFace != null)
      _face = connectorFace;
    if (properties != null)
      _resistance = properties["resistance"].AsFloat(DefaultResistance);
  }

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);

    // The base seeds only the single OutFacingForNetworkDiscovery face. Couple the opposite end of
    // the axis too, so a row of ports merges into one network and power passes straight through: an
    // axle on either side drives the same line.
    if (api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null)
      tryConnect(OutFacingForNetworkDiscovery.Opposite);
  }

  public override float GetResistance() => _resistance;

  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = _face;
    // One sign per axis, not per facing: opposite facings share an axle line and must not
    // counter-rotate.
    AxisSign = _face.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  /// <summary>The filler is invisible; the principal renders the rotor, so the port adds no mesh.</summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) => false;
}

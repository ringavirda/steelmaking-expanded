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
  /// <summary>Load the walking beam puts on the shaft; a little above a bare port, below a hammer.</summary>
  public const float PumpResistance = 0.6f;

  /// <summary>The network's rotation speed, absolute; 0 when no axle is coupled.</summary>
  public float DriveSpeed => Network != null ? Math.Abs(Network.Speed) : 0f;

  /// <summary>True while the axle is turning, that is, while the beam is being worked.</summary>
  public bool IsTurning => Network is { Speed: > 0.001f or < -0.001f };

  public override float GetResistance() => PumpResistance;

  public override void SetOrientations() {
    OutFacingForNetworkDiscovery = ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide(Block.Variant["side"])
    );
    // One sign per axis, not per facing: opposite facings share an axle line and must not
    // counter-rotate.
    AxisSign =
      OutFacingForNetworkDiscovery.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);

    // The base seeds only the single discovery face; couple the opposite end of the axis too so an
    // axle can run straight through and drive the pump from either side.
    if (api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null)
      tryConnect(OutFacingForNetworkDiscovery.Opposite);
  }

  /// <summary>The pump block entity draws the beam and gear, so the behaviour adds no mesh.</summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) => false;
}

using System;
using System.Linq;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Mechanical-power <b>producer</b> for the MP-generator sub-machine — the mod's first torque
/// source (modelled on the vanilla rotor rather than the consumer transmission). It runs as a
/// constant-power source off the engine's <see cref="BlockEntityEngine.MpPowerBudget"/>, so the
/// network settles at <c>speed = budget / load</c>: the rated speed at the rated load, slower
/// under heavier loads. In its natural (north) orientation the axle couples on the north/south faces.
/// </summary>
[EntityRegister]
public class BEBehaviorEngineMPGenerator(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity)
{
  private MeshData? _baseMesh;

  public override void Initialize(ICoreAPI api, JsonObject properties)
  {
    base.Initialize(api, properties);

    // The generator couples on BOTH ends of its axis and is the only power source on the line,
    // so a passive axle line on the back face has nothing to re-discover it on reload — the base
    // only seeds the single OutFacingForNetworkDiscovery face, leaving the other side dead. Wire
    // the opposite connector too, exactly as vanilla's angled gears do for their second face.
    if (api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null)
      tryConnect(OutFacingForNetworkDiscovery.Opposite);
  }

  /// <summary>
  /// Re-applies the axle orientation after the block's side variant changed (the engine snapped
  /// the generator to its matching facing via <c>ExchangeBlock</c>, which keeps this behavior
  /// alive — <see cref="Initialize"/> never re-runs). Rebuilds the rotated static base mesh and
  /// re-seeds the network connectors onto the new axis, mirroring the seeding done at init.
  /// </summary>
  public void OnOrientationChanged()
  {
    _baseMesh = null;
    SetOrientations();
    if (Api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null)
    {
      tryConnect(OutFacingForNetworkDiscovery);
      tryConnect(OutFacingForNetworkDiscovery.Opposite);
    }
    Blockentity.MarkDirty(true);
  }

  public override float GetResistance() => 0.0005f;

  public override float GetTorque(long tick, float speed, out float resistance)
  {
    resistance = 0f;
    var engine = (Blockentity as BlockEntityEngineMpGenerator)?.Engine;
    float budget = engine?.MpPowerBudget ?? 0f;
    if (budget <= 0f)
      return 0f;

    // Constant-power source: deliver the engine's fixed power budget at the current speed
    // (torque = budget / speed), so the network settles where this matches the consumers'
    // resistance — i.e. speed = budget / load. At the rated load this balances exactly at the
    // rated speed; heavier loads run slower. Clamp the divisor so spinning up from rest asks for
    // bounded torque. Torque stays positive — the vanilla network only animates while speed >= 0,
    // so rotation DIRECTION lives in the discovery seed / AxisSign (see SetOrientations).
    float ratedSpeed = PpexValues.MpRatedSpeed;
    float torque = budget / Math.Max(speed, 0.25f * ratedSpeed);

    // Soft top-speed cap: a light load would otherwise let the constant-power curve run the line
    // far past the rated speed. Taper the torque to zero between the rated speed and 1.5× it, so a
    // lightly-loaded line settles just above rated and an unloaded one near 1.5× — WITHOUT the
    // abrupt cut a hard cap would make, which sawtooths the speed right at the rated load.
    float capEnd = 1.5f * ratedSpeed;
    if (speed >= capEnd)
      return 0f;
    if (speed > ratedSpeed)
      torque *= (capEnd - speed) / (capEnd - ratedSpeed);
    return torque;
  }

  public override void SetOrientations()
  {
    // Seed network discovery from the BACK of the axis (south / west). The discovery direction
    // sets every node's propagationDir, which drives vanilla's IsRotationReversed — so seeding
    // from the far end reverses the whole shaft's rendered spin (generator AND every connected
    // axle together, staying consistent) relative to seeding from the near end. The near end
    // turned the shaft opposite the engine's beam linkage; the far end matches it.
    OutFacingForNetworkDiscovery = Block.Variant["side"] switch
    {
      "north" or "south" => BlockFacing.SOUTH,
      "east" or "west" => BlockFacing.WEST,
      _ => BlockFacing.SOUTH,
    };

    // Single sign per axis matching the vanilla axle convention ({-1} on each axis, as used by
    // the wooden axle / bessemer transmission / blower), so the generator's rendered axle shares
    // the connected line's rotation convention and co-rotates with it instead of fighting it.
    AxisSign =
      OutFacingForNetworkDiscovery.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  protected override CompositeShape GetShape() =>
    new()
    {
      Base = Block.Shape.Base.Clone(),
      SelectiveElements = ["Axle*"],
      rotateY = Block.Shape.rotateY,
      InsertBakedTextures = true,
    };

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  )
  {
    if (_baseMesh == null)
    {
      AssetLocation shapeLoc = Block
        .Shape.Base.WithPathPrefixOnce("shapes/")
        .WithPathAppendixOnce(".json");
      Shape? shape = Api.Assets.TryGet(shapeLoc)?.ToObject<Shape>();
      if (shape != null)
      {
        Shape baseShape = shape.Clone();
        baseShape.Elements = baseShape
          .Elements.Where(e => !e.Name?.StartsWith("Axle") ?? true)
          .ToArray();
        tesselator.TesselateShape(Block, baseShape, out _baseMesh);

        float rotY = Block.Shape.rotateY * GameMath.DEG2RAD;
        if (rotY != 0 && _baseMesh != null)
          _baseMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0, rotY, 0);
      }
    }

    if (_baseMesh != null)
      mesher.AddMeshData(_baseMesh);

    base.OnTesselation(mesher, tesselator);
    return true;
  }
}

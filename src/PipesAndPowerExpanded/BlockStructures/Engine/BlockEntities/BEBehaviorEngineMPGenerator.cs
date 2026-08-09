using System;
using System.Linq;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Mechanical-power <b>producer</b> for the MP-generator sub-machine - the mod's first torque
/// source (modelled on the vanilla rotor rather than the consumer transmission). It runs as a
/// constant-power source off the engine's <see cref="BlockEntityEngine.MpPowerBudget"/>, so the
/// network settles at <c>speed = budget / load</c>: the rated speed at the rated load, slower
/// under heavier loads. In its natural (north) orientation the axle couples on the north/south faces.
/// </summary>
[BlockEntityBehaviorRegister]
public class BEBehaviorEngineMPGenerator(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity) {
  private MeshData? _baseMesh;

  public override void Initialize(ICoreAPI api, JsonObject properties) {
    base.Initialize(api, properties);

    // The generator couples on BOTH ends of its axis, but the base only seeds the single
    // OutFacingForNetworkDiscovery face. Wire the opposite connector too (like vanilla's angled gears).
    if (api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null)
      tryConnect(OutFacingForNetworkDiscovery.Opposite);
  }

  /// <summary>
  /// Re-applies the axle orientation after a side-variant change (the engine snapped the generator
  /// via <c>ExchangeBlock</c>, keeping this behavior alive so <see cref="Initialize"/> never re-runs).
  /// Rebuilds the rotated base mesh and re-seeds the connectors onto the new axis.
  /// </summary>
  public void OnOrientationChanged() {
    _baseMesh = null;
    SetOrientations();
    if (Api.Side == EnumAppSide.Server && OutFacingForNetworkDiscovery != null) {
      tryConnect(OutFacingForNetworkDiscovery);
      tryConnect(OutFacingForNetworkDiscovery.Opposite);
    }
    Blockentity.MarkDirty(true);
  }

  public override float GetResistance() => 0.0005f;

  /// <summary>
  /// Summed rated MP load of every engine currently driving this shaft - this engine's own rating
  /// when the network is not resolved yet. The overstress ceiling has to be judged against what is
  /// actually turning the shaft: measuring a shared network's whole resistance against ONE engine's
  /// rating froze the ceiling at a single engine's worth, so adding engines could not raise it and
  /// a bank stalled well below what it could really drive.
  /// </summary>
  public float DrivenRatedLoad {
    get {
      float own =
        (Blockentity as BlockEntityEngineMpGenerator)?.Engine?.MpRatedLoad
        ?? 0f;
      if (Network == null)
        return own;

      float total = 0f;
      foreach (var node in Network.nodes.Values)
        if (
          node is BEBehaviorEngineMPGenerator gen
          && (gen.Blockentity as BlockEntityEngineMpGenerator)?.Engine is { } e
        )
          total += e.MpRatedLoad;

      return total > 0f ? total : own;
    }
  }

  public override float GetTorque(long tick, float speed, out float resistance) {
    resistance = 0f;
    var engine = (Blockentity as BlockEntityEngineMpGenerator)?.Engine;
    float budget = engine?.MpPowerBudget ?? 0f;
    if (budget <= 0f)
      return 0f;

    // Constant-power source: torque = budget / speed, so the network settles at speed = budget /
    // load (rated speed at rated load, slower under more). Clamp the divisor so spin-up asks bounded
    // torque. Torque stays positive - direction lives in the discovery seed / AxisSign.
    float ratedSpeed = PpexValues.MpRatedSpeed;
    float torque = budget / Math.Max(speed, 0.25f * ratedSpeed);

    // Soft top-speed cap: taper torque to zero between rated speed and 1.5× it, so a light load
    // settles just above rated (an unloaded line near 1.5×) without the sawtooth a hard cap makes.
    float capEnd = 1.5f * ratedSpeed;
    if (speed >= capEnd)
      return 0f;
    if (speed > ratedSpeed)
      torque *= (capEnd - speed) / (capEnd - ratedSpeed);
    return torque;
  }

  public override void SetOrientations() {
    // Seed discovery from the BACK of the axis (south/west). The discovery direction drives
    // vanilla's IsRotationReversed, so the far end reverses the whole shaft's rendered spin to
    // match the engine's beam linkage (the near end turned it the opposite way).
    OutFacingForNetworkDiscovery = Block.Variant["side"] switch {
      "north" or "south" => BlockFacing.SOUTH,
      "east" or "west" => BlockFacing.WEST,
      _ => BlockFacing.SOUTH,
    };

    // Single sign per axis matching the vanilla axle convention, so the rendered axle co-rotates
    // with the connected line instead of fighting it.
    AxisSign =
      OutFacingForNetworkDiscovery.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  protected override CompositeShape GetShape() =>
    new() {
      Base = Block.Shape.Base.Clone(),
      SelectiveElements = ["Axle*"],
      rotateY = Block.Shape.rotateY,
      InsertBakedTextures = true,
    };

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) {
    if (_baseMesh == null) {
      AssetLocation shapeLoc = Block
        .Shape.Base.WithPathPrefixOnce("shapes/")
        .WithPathAppendixOnce(".json");
      Shape? shape = Api.Assets.TryGet(shapeLoc)?.ToObject<Shape>();
      if (shape != null) {
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

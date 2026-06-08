using System.Linq;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Mechanical-power <b>producer</b> for the Cornish MP-generator sub-machine — the
/// mod's first torque source (modelled on the vanilla rotor rather than the
/// consumer transmission). It injects torque proportional to the engine's available
/// power, tapering as the network speeds up so it settles at a power-dependent speed.
/// In its natural (north) orientation the axle couples on the north/south faces.
/// </summary>
[EntityRegister]
public class BEBehaviorEngineMPGenerator(BlockEntity blockentity)
  : BEBehaviorMPBase(blockentity)
{
  private MeshData? _baseMesh;

  private float EnginePower =>
    (Blockentity as BlockEntityEngineMpGenerator)?.Engine?.AvailablePower ?? 0f;

  public override float GetResistance() => 0.0005f;

  public override float GetTorque(long tick, float speed, out float resistance)
  {
    resistance = 0f;
    float power = EnginePower;
    if (power <= 0f)
      return 0f;

    // Target a network speed proportional to power; produce torque to close the gap
    // and stop pushing once at/above target (so the wheel settles, not runs away).
    float targetSpeed = power; // power is 0..CornishEngineMaxPower
    float gap = targetSpeed - speed;
    if (gap <= 0f)
      return 0f;
    return GameMath.Clamp(gap, 0f, targetSpeed)
      * PpexValues.CornishEngineMaxPower
      * 4f;
  }

  public override void SetOrientations()
  {
    OutFacingForNetworkDiscovery = Block.Variant["side"] switch
    {
      "north" or "south" => BlockFacing.NORTH,
      "east" or "west" => BlockFacing.EAST,
      _ => BlockFacing.NORTH,
    };

    // Single sign per axis so the rendered axle doesn't counter-rotate against the
    // line it drives (same convention as the bessemer transmission / old blower).
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

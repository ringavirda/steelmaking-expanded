using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// Mechanical-power node for the gas blower. Couples an axle on the faces
/// perpendicular to the blower's gas-flow axis and turns with the network so the
/// <see cref="BlockEntityGasBlower"/> can read its speed to set the transfer rate.
/// </summary>
public class BEBehaviorMPBlower : BEBehaviorMPBase
{
  private MeshData? _baseMesh;

  /// <summary>Orientation the cached <see cref="_baseMesh"/> was built for; used to
  /// detect a wrench rotation (which swaps the block but keeps this BE) and rebuild.</summary>
  private string? _baseMeshOrientation;

  public BEBehaviorMPBlower(BlockEntity blockentity)
    : base(blockentity) { }

  public override float GetResistance() => 0.001f;

  public override void SetOrientations()
  {
    string orient = Block.Variant["orientation"];
    if (orient == "ns")
    {
      AxisSign = [-1, 0, 0];
      OutFacingForNetworkDiscovery = BlockFacing.WEST;
    }
    else if (orient == "we")
    {
      AxisSign = [0, 0, -1];
      OutFacingForNetworkDiscovery = BlockFacing.NORTH;
    }
  }

  protected override CompositeShape GetShape()
  {
    return new CompositeShape()
    {
      Base = Block.Shape.Base.Clone(),
      SelectiveElements = ["Axle*"],
      rotateY = Block.Shape.rotateY,
      InsertBakedTextures = true,
    };
  }

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  )
  {
    // Rebuild when the orientation changes: a wrench rotation exchanges the block
    // but reuses this BE, so a cached base mesh would stay in the old pose while
    // the axle (rebuilt from the synced shape) turns to the new one.
    string orient = Block.Variant["orientation"];
    if (_baseMesh == null || _baseMeshOrientation != orient)
    {
      _baseMeshOrientation = orient;
      AssetLocation shapeLoc = Block
        .Shape.Base.WithPathPrefixOnce("shapes/")
        .WithPathAppendixOnce(".json");
      Shape shape = Api.Assets.Get<Shape>(shapeLoc);
      if (shape != null)
      {
        Shape baseShape = shape.Clone();
        baseShape.Elements = baseShape
          .Elements.Where(e => !e.Name?.StartsWith("Axle") ?? true)
          .ToArray();
        tesselator.TesselateShape(Block, baseShape, out _baseMesh);
        if (Block.Shape != null)
        {
          float rotX = Block.Shape.rotateX * GameMath.DEG2RAD;
          float rotY = Block.Shape.rotateY * GameMath.DEG2RAD;
          float rotZ = Block.Shape.rotateZ * GameMath.DEG2RAD;

          if (rotX != 0 || rotY != 0 || rotZ != 0)
          {
            Vec3f center = new(0.5f, 0.5f, 0.5f);
            _baseMesh.Rotate(center, rotX, rotY, rotZ);
          }
        }
      }
    }

    if (_baseMesh != null)
    {
      mesher.AddMeshData(_baseMesh);
    }

    base.OnTesselation(mesher, tesselator);
    return true;
  }
}

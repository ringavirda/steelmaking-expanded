using System.Linq;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SteelmakingExpanded.BlockStructures.Converter.BlockEntities;

/// <summary>
/// Mechanical-power node for the Bessemer transmission. In its natural (north)
/// orientation the axle couples on the south face; the connector rotates with
/// the block's "side" variant. The transmission is a network endpoint — it only
/// feeds the converter's control block, which reads the network speed.
/// </summary>
[EntityRegister]
public class BEBehaviorMPConverterTransmission : BEBehaviorMPBase
{
  private MeshData? _baseMesh;

  public BEBehaviorMPConverterTransmission(BlockEntity blockentity)
    : base(blockentity) { }

  public override float GetResistance() => 0.05f;

  public override void SetOrientations()
  {
    OutFacingForNetworkDiscovery = Block.Variant["side"] switch
    {
      "north" => BlockFacing.SOUTH,
      "east" => BlockFacing.WEST,
      "south" => BlockFacing.NORTH,
      "west" => BlockFacing.EAST,
      _ => BlockFacing.NORTH,
    };

    // The rendered axle must spin the same way as the axle line feeding it.
    // Rotation sense is a property of the AXIS, not the facing direction:
    // opposite facings on the same axis (north↔south, east↔west) share one axle
    // line and must not counter-rotate. Using the signed facing normal flips the
    // sense for half the orientations (the north/west reversal), so collapse to a
    // single sign per axis — the same -1 convention the gas blower uses.
    AxisSign =
      OutFacingForNetworkDiscovery.Axis == EnumAxis.X ? [-1, 0, 0] : [0, 0, -1];
  }

  protected override CompositeShape GetShape()
  {
    return new CompositeShape
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

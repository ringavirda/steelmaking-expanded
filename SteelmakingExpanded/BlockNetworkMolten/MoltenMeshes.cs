using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>
/// Shared mesh work for the molten-canal family. Currently the open-end cap: the canal,
/// tap and pedestal all cap unconnected (or closed) connector faces with the same
/// <c>end.json</c> piece, rotated to the face — previously three copies of the same
/// tesselate-and-rotate block.
/// </summary>
public static class MoltenMeshes
{
  private static readonly AssetLocation EndCapShapeLoc = new(
    "smex:shapes/molten/canal/end.json"
  );

  /// <summary>Y rotation (degrees) that points the end-cap shape at <paramref name="face"/> (authored facing south).</summary>
  public static float EndCapRotYDeg(BlockFacing face) =>
    face.Index switch
    {
      BlockFacing.indexNORTH => 180f,
      BlockFacing.indexEAST => 90f,
      BlockFacing.indexWEST => 270f,
      _ => 0f,
    };

  /// <summary>
  /// Tesselates the canal end-cap for <paramref name="block"/>, rotated to cap
  /// <paramref name="face"/>. Returns <c>null</c> when the shape asset is missing.
  /// Callers cache the result (per face or per state) themselves.
  /// </summary>
  public static MeshData? TesselateEndCap(
    ICoreAPI api,
    ITesselatorAPI tesselator,
    Block block,
    BlockFacing face
  )
  {
    var endShape = api.Assets.Get<Shape>(EndCapShapeLoc);
    if (endShape == null)
      return null;

    tesselator.TesselateShape(block, endShape, out MeshData endMesh);
    float rotY = EndCapRotYDeg(face) * GameMath.DEG2RAD;
    if (rotY != 0f)
      endMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, rotY, 0f);
    return endMesh;
  }
}

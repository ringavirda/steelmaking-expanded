using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib;

/// <summary>
/// The single source of truth for the mod family's horizontal rotation math. Every
/// oriented block (boilers, engines, converters, cowper stoves, smoke stacks) places its
/// fillers, connectors, particle boxes and sub-cells relative to a "north" (0°) layout and
/// then rotates by the structure's angle. Keeping that rotation in one place means a block
/// only has to pick the right <em>angle</em> — the translation itself is identical
/// everywhere, so orientation bugs no longer have to be chased per block.
/// <para>
/// All methods share one convention (matching
/// <see cref="BlockStructures.BlockEntityMultiblockStructure.GetGlobalPos"/>):
/// north 0°, west 90°, south 180°, east 270°, with
/// <c>(x,z) → 90:(z,-x) · 180:(-x,-z) · 270:(-z,x)</c>.
/// </para>
/// </summary>
public static class ExOrientation
{
  /// <summary>
  /// Maps a horizontal "side" variant to its rotation angle (north 0, west 90, south 180,
  /// east 270). Accepts both the full names used by <c>side</c> variants and the
  /// single-letter codes used by <c>orientation</c> variants ("n"/"w"/"s"/"e").
  /// </summary>
  public static int AngleFromSide(string? side) =>
    side switch
    {
      "east" or "e" => 270,
      "south" or "s" => 180,
      "west" or "w" => 90,
      _ => 0, // "north"/"n" or default
    };

  /// <summary>Rotates a structure-local offset by <paramref name="angle"/> (Y is untouched).</summary>
  public static Vec3i RotateOffset(Vec3i off, int angle) =>
    RotateOffset(off.X, off.Y, off.Z, angle);

  /// <summary>Rotates a structure-local offset by <paramref name="angle"/> (Y is untouched).</summary>
  public static Vec3i RotateOffset(int x, int y, int z, int angle)
  {
    // Normalise first so callers can pass any multiple/offset (e.g. Angle + 180 → 450)
    // without it slipping past the 90/180/270 cases into the unrotated default.
    angle = ((angle % 360) + 360) % 360;
    var (dx, dz) = angle switch
    {
      90 => (z, -x),
      180 => (-x, -z),
      270 => (-z, x),
      _ => (x, z), // 0° or any unhandled value
    };
    return new Vec3i(dx, y, dz);
  }

  /// <summary>
  /// Converts a structure-local offset into a world position for the given rotation:
  /// <c>origin + RotateOffset(local, angle)</c>. This is the body shared by every machine's
  /// <c>GetGlobalPos</c>.
  /// </summary>
  public static BlockPos GlobalPos(
    BlockPos origin,
    int localX,
    int localY,
    int localZ,
    int angle
  )
  {
    Vec3i r = RotateOffset(localX, localY, localZ, angle);
    return origin.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>
  /// Reads a single structure-local <c>{ x, y, z }</c> offset from a block's JSON
  /// attributes (e.g. <c>submachineOffset</c>), falling back to <paramref name="fallback"/>
  /// when the attribute is absent. Keeps placement offsets data-driven and in one format.
  /// </summary>
  public static Vec3i ReadOffset(Block block, string attr, Vec3i fallback)
  {
    var node = block.Attributes?[attr];
    if (node == null || !node.Exists)
      return fallback;
    return new Vec3i(
      node["x"].AsInt(fallback.X),
      node["y"].AsInt(fallback.Y),
      node["z"].AsInt(fallback.Z)
    );
  }

  /// <summary>
  /// Reads a single structure-local <c>{ x, y, z }</c> offset with fractional (block-unit
  /// double) coordinates from a block's JSON attributes (e.g. <c>cylinderVentOffset</c>),
  /// falling back to <paramref name="fallback"/> when the attribute is absent. The double
  /// counterpart of <see cref="ReadOffset"/> for continuous points (particle anchors).
  /// </summary>
  public static Vec3d ReadOffsetD(Block block, string attr, Vec3d fallback)
  {
    var node = block.Attributes?[attr];
    if (node == null || !node.Exists)
      return fallback;
    return new Vec3d(
      node["x"].AsDouble(fallback.X),
      node["y"].AsDouble(fallback.Y),
      node["z"].AsDouble(fallback.Z)
    );
  }

  /// <summary>
  /// Resolves an attribute-declared structure-local offset straight to a world cell:
  /// <c>origin + RotateOffset(ReadOffset(attr), angle)</c>. The one canonical way a
  /// machine block turns a JSON offset (firebox, lid, port, sub-machine cell, …) into
  /// the world position for its placed rotation.
  /// </summary>
  public static BlockPos WorldPosFromAttr(
    Block block,
    BlockPos origin,
    string attr,
    Vec3i fallback,
    int angle
  )
  {
    Vec3i off = ReadOffset(block, attr, fallback);
    Vec3i r = RotateOffset(off, angle);
    return origin.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>
  /// Returns copies of <paramref name="boxes"/> rotated around the block centre by
  /// <paramref name="angle"/> degrees (Y axis). JSON collision/selection boxes are
  /// authored in the north orientation and do not auto-rotate with a placed "side"
  /// variant, so oriented port blocks rotate them to match the shape's rotateY.
  /// Returns the input array unchanged for angle 0.
  /// </summary>
  public static Cuboidf[] RotateBoxes(Cuboidf[] boxes, int angle)
  {
    angle = ((angle % 360) + 360) % 360;
    if (angle == 0 || boxes.Length == 0)
      return boxes;
    var origin = new Vec3d(0.5, 0.5, 0.5);
    var rotated = new Cuboidf[boxes.Length];
    for (int i = 0; i < boxes.Length; i++)
      rotated[i] = boxes[i].RotatedCopy(0, angle, 0, origin);
    return rotated;
  }

  /// <summary>
  /// Rotates a horizontal block face by <paramref name="angle"/> (same convention as
  /// <see cref="RotateOffset(Vec3i, int)"/>). Vertical faces (up/down) are returned
  /// unchanged. Used to map north-orientation connector faces onto the placed orientation.
  /// </summary>
  public static BlockFacing RotateFacing(BlockFacing baseFace, int angle)
  {
    if (baseFace.IsVertical)
      return baseFace;
    Vec3i n = baseFace.Normali;
    Vec3i r = RotateOffset(new Vec3i(n.X, 0, n.Z), angle);
    return BlockFacing.FromNormal(r) ?? baseFace;
  }

  /// <summary>
  /// Rotates a block-relative float coordinate around a cell centre by
  /// <paramref name="angle"/> — the same rotation as <see cref="RotateOffset(Vec3i, int)"/>
  /// but for continuous coordinates (particle/render boxes). The caller supplies whichever
  /// angle source is correct for its case (the visual <c>Shape.rotateY</c> or a structure
  /// angle); the rotation itself is convention-agnostic.
  /// </summary>
  public static void RotateAroundCenter(
    ref float x,
    ref float z,
    int angle,
    float center = 0.5f
  )
  {
    angle = ((angle % 360) + 360) % 360;
    float dx = x - center;
    float dz = z - center;
    var (ndx, ndz) = angle switch
    {
      90 => (dz, -dx),
      180 => (-dx, -dz),
      270 => (-dz, dx),
      _ => (dx, dz),
    };
    x = center + ndx;
    z = center + ndz;
  }
}

using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Helpers;

/// <summary>
/// Single source of truth for the mod family's horizontal rotation math. Every oriented block
/// places its fillers, connectors, particle boxes and sub-cells relative to a "north" (0°) layout
/// and rotates by the structure's angle, so a block only has to pick the right <em>angle</em>. All
/// methods share one convention: north 0°, west 90°, south 180°, east 270°, with
/// <c>(x,z) → 90:(z,-x) · 180:(-x,-z) · 270:(-z,x)</c>.
/// </summary>
public static class ExOrientation {
  /// <summary>
  /// Maps a horizontal "side" variant to its rotation angle (north 0, west 90, south 180,
  /// east 270). Accepts both the full names used by <c>side</c> variants and the
  /// single-letter codes used by <c>orientation</c> variants ("n"/"w"/"s"/"e").
  /// </summary>
  public static int AngleFromSide(string? side) =>
    side switch {
      "east" or "e" => 270,
      "south" or "s" => 180,
      "west" or "w" => 90,
      _ => 0, // "north"/"n" or default
    };

  /// <summary>Rotates a structure-local offset by <paramref name="angle"/> (Y is untouched).</summary>
  public static Vec3i RotateOffset(Vec3i off, int angle) =>
    RotateOffset(off.X, off.Y, off.Z, angle);

  /// <summary>Rotates a structure-local offset by <paramref name="angle"/> (Y is untouched).</summary>
  public static Vec3i RotateOffset(int x, int y, int z, int angle) {
    // Normalise first so callers can pass any multiple/offset (e.g. Angle + 180 → 450)
    // without it slipping past the 90/180/270 cases into the unrotated default.
    angle = ((angle % 360) + 360) % 360;
    var (dx, dz) = angle switch {
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
  ) {
    Vec3i r = RotateOffset(localX, localY, localZ, angle);
    return origin.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>
  /// Reads a structure-local <c>{ x, y, z }</c> offset from an already-resolved JSON node (e.g. a
  /// block's generated offset accessor), falling back to <paramref name="fallback"/> when absent.
  /// </summary>
  public static Vec3i ReadOffset(JsonObject? node, Vec3i fallback) {
    if (node == null || !node.Exists)
      return fallback;
    return new Vec3i(
      node["x"].AsInt(fallback.X),
      node["y"].AsInt(fallback.Y),
      node["z"].AsInt(fallback.Z)
    );
  }

  /// <summary>
  /// The fractional (double) counterpart of <see cref="ReadOffset"/>, for continuous points
  /// (particle anchors). Falls back to <paramref name="fallback"/> when the node is absent.
  /// </summary>
  public static Vec3d ReadOffsetD(JsonObject? node, Vec3d fallback) {
    if (node == null || !node.Exists)
      return fallback;
    return new Vec3d(
      node["x"].AsDouble(fallback.X),
      node["y"].AsDouble(fallback.Y),
      node["z"].AsDouble(fallback.Z)
    );
  }

  /// <summary>
  /// Resolves an already-resolved structure-local offset node to a world cell:
  /// <c>origin + RotateOffset(ReadOffset(node), angle)</c>. The canonical way a machine turns a
  /// JSON offset (passed via its generated accessor) into a world position for its placed rotation.
  /// </summary>
  public static BlockPos WorldPosFromAttr(
    BlockPos origin,
    JsonObject? node,
    Vec3i fallback,
    int angle
  ) {
    Vec3i off = ReadOffset(node, fallback);
    Vec3i r = RotateOffset(off, angle);
    return origin.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>
  /// Returns copies of <paramref name="boxes"/> rotated around the block centre by
  /// <paramref name="angle"/>° (Y axis). JSON boxes are authored north-facing and don't auto-rotate
  /// with the "side" variant, so port blocks rotate them to match. Unchanged for angle 0.
  /// </summary>
  public static Cuboidf[] RotateBoxes(Cuboidf[] boxes, int angle) {
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
  public static BlockFacing RotateFacing(BlockFacing baseFace, int angle) {
    if (baseFace.IsVertical)
      return baseFace;
    Vec3i n = baseFace.Normali;
    Vec3i r = RotateOffset(new Vec3i(n.X, 0, n.Z), angle);
    return BlockFacing.FromNormal(r) ?? baseFace;
  }

  /// <summary>
  /// Rotates a block-relative float coordinate around a cell centre by <paramref name="angle"/> -
  /// the continuous-coordinate counterpart of <see cref="RotateOffset(Vec3i, int)"/> (particle/
  /// render boxes). The caller supplies the correct angle source.
  /// </summary>
  public static void RotateAroundCenter(
    ref float x,
    ref float z,
    int angle,
    float center = 0.5f
  ) {
    angle = ((angle % 360) + 360) % 360;
    float dx = x - center;
    float dz = z - center;
    var (ndx, ndz) = angle switch {
      90 => (dz, -dx),
      180 => (-dx, -dz),
      270 => (-dz, dx),
      _ => (dx, dz),
    };
    x = center + ndx;
    z = center + ndz;
  }
}

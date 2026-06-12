using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>Defines a horizontal fill footprint (x/z extents) for the molten-surface renderer.</summary>
public struct FillQuadDef
{
  public float x1,
    z1,
    x2,
    z2;
}

/// <summary>
/// Reads the molten-surface fill geometry attributes (<c>fillQuadsByLevel</c>,
/// <c>fillStart</c>, <c>fillHeight</c> — or a prefixed variant like the pedestal's
/// <c>moldFill*</c>) that every molten container block declares for its
/// <see cref="MoltenRenderer"/>. One reader for the canal, tap, pedestal and barrel,
/// which each carried their own copy of this parsing.
/// </summary>
public static class FillQuads
{
  /// <summary>
  /// The renderer footprint boxes from <paramref name="attr"/> (each quad def becomes a
  /// full-height x/z box), or a single <paramref name="fallback"/> box when the
  /// attribute is absent.
  /// </summary>
  public static Cuboidf[] ReadBoxes(Block? block, string attr, Cuboidf fallback)
  {
    var quadDefs = block?.Attributes?[attr]?.AsObject<FillQuadDef[]>();
    return quadDefs is { Length: > 0 }
      ? [.. quadDefs.Select(q => new Cuboidf(q.x1, 0f, q.z1, q.x2, 16f, q.z2))]
      : [fallback];
  }

  /// <summary>The fill-surface base height in block units (the attribute is in pixels).</summary>
  public static float ReadStartY(Block? block, string attr, float fallbackPx) =>
    (block?.Attributes?[attr]?.AsFloat(fallbackPx) ?? fallbackPx) / 16f;

  /// <summary>The fill-surface travel, in pixel levels.</summary>
  public static float ReadHeightLevels(
    Block? block,
    string attr,
    float fallback
  ) => block?.Attributes?[attr]?.AsFloat(fallback) ?? fallback;
}

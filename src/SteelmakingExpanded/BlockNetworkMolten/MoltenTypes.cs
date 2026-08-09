using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>
/// Defines a horizontal fill footprint (x/z extents) for the molten-surface
/// renderer.
/// </summary>
public struct FillQuadDef {
  public float x1,
    z1,
    x2,
    z2;
}

/// <summary>
/// Reads the molten-surface fill geometry attributes (<c>fillQuadsByLevel</c>,
/// <c>fillStart</c>, <c>fillHeight</c> - or a prefixed variant like the pedestal's
/// <c>moldFill*</c>) that every molten container block declares for its
/// <see cref="MoltenRenderer"/>. One reader for the canal, tap, pedestal and barrel,
/// which each carried their own copy of this parsing.
/// </summary>
public static class FillQuads {
  /// <summary>
  /// Builds the renderer footprint boxes from an already-resolved <c>fillQuadsByLevel</c> node - e.g.
  /// a block's generated <c>FillQuadsByLevel</c> accessor - so the caller passes a typed value
  /// instead of an attribute-name string. Each quad def becomes a full-height x/z box; a missing or
  /// empty node yields a single <paramref name="fallback"/> box.
  /// <para>
  /// The fill <em>start</em> (pixels → block units via <c>/16f</c>) and <em>height levels</em> are now
  /// read directly from the block's generated <c>FillStart</c>/<c>FillHeight</c> consts at the call
  /// site, so there are no <c>ReadStartY</c>/<c>ReadHeightLevels</c> helpers anymore.
  /// </para>
  /// </summary>
  public static Cuboidf[] BoxesFrom(JsonObject? quadsNode, Cuboidf fallback) {
    var quadDefs = quadsNode?.AsObject<FillQuadDef[]>();
    return quadDefs is { Length: > 0 }
      ? [.. quadDefs.Select(q => new Cuboidf(q.x1, 0f, q.z1, q.x2, 16f, q.z2))]
      : [fallback];
  }
}

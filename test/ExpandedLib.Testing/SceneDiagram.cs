using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// Turns an ASCII layout into block placements, so a multi-network integration setup reads like the
/// thing it models instead of a wall of <c>Place(new BlockPos(...))</c> calls. Each non-blank glyph is
/// mapped by a <em>legend</em> to a placement action; a layer is one horizontal (X/Z) plane at a given
/// height, and several layers stack a 3D setup.
/// <para>
/// Coordinate convention: a row's characters advance along +X (columns), successive rows advance along
/// +Z, and each <see cref="Layer"/> sits at its own Y. So
/// <code>
///   diagram.On('=', p =&gt; scene.Node(p, WePipe(), new BlockEntityPipe(), "pipe"))
///          .On('#', p =&gt; scene.Block(p, Rock))
///          .Layer("#====#");
/// </code>
/// lays a five-cell west-east pipe run between two rock caps along the X axis.
/// </para>
/// The legend is supplied by the test (it is mod-specific - which glyph means which oriented pipe,
/// canal, machine, or cap), keeping this parser itself mod-agnostic.
/// </summary>
public sealed class SceneDiagram {
  private readonly Dictionary<char, System.Action<BlockPos>> _legend = new();

  /// <summary>Maps a glyph to the action that places it at a resolved world position.</summary>
  public SceneDiagram On(char glyph, System.Action<BlockPos> place) {
    _legend[glyph] = place;
    return this;
  }

  /// <summary>
  /// Applies one horizontal layer of the diagram at height <paramref name="y"/>, with the top-left
  /// glyph at (<paramref name="originX"/>, <paramref name="y"/>, <paramref name="originZ"/>). Blank
  /// spaces and unmapped glyphs are skipped, so labels/gaps are free. Leading/trailing blank lines
  /// (e.g. from a verbatim string) are trimmed.
  /// </summary>
  public SceneDiagram Layer(
    string ascii,
    int y = 0,
    int originX = 0,
    int originZ = 0
  ) {
    string[] rows = ascii.Replace("\r", "").Trim('\n').Split('\n');
    for (int row = 0; row < rows.Length; row++) {
      string line = rows[row];
      for (int col = 0; col < line.Length; col++)
        if (_legend.TryGetValue(line[col], out var place))
          place(new BlockPos(originX + col, y, originZ + row));
    }
    return this;
  }

  /// <summary>
  /// Stacks several horizontal layers along the Y axis in one call - the natural way to lay out a
  /// structure or manifold that spans height (a riser, a multi-floor furnace, a 3D pipe junction).
  /// <paramref name="layers"/>[0] sits at <paramref name="baseY"/>, [1] at <c>baseY + 1</c>, and so on
  /// (bottom-to-top); each entry is itself a multi-line X/Z plane in the same format as
  /// <see cref="Layer"/>. All layers share the (<paramref name="originX"/>, <paramref name="originZ"/>)
  /// origin so the columns line up vertically.
  /// <code>
  ///   diagram.Stack(baseY: 0,
  ///     "#I#",   // y=0  (bottom)
  ///     " I ",   // y=1
  ///     "=I=");  // y=2  (top) - a vertical riser meeting a west-east main
  /// </code>
  /// </summary>
  public SceneDiagram Stack(
    int baseY,
    int originX,
    int originZ,
    params string[] layers
  ) {
    for (int i = 0; i < layers.Length; i++)
      Layer(layers[i], baseY + i, originX, originZ);
    return this;
  }

  /// <summary>Stacks layers from <paramref name="baseY"/> upward at origin (0,0); see the fuller overload.</summary>
  public SceneDiagram Stack(int baseY, params string[] layers) =>
    Stack(baseY, 0, 0, layers);
}

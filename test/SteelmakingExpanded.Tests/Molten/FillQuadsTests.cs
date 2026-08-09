using Newtonsoft.Json.Linq;
using SteelmakingExpanded.BlockNetworkMolten;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The shared molten-surface fill-geometry reader. A missing/empty <c>fillQuadsByLevel</c> node
/// yields a single fallback box; a populated one becomes one full-height x/z box per quad def.
/// </summary>
public class FillQuadsTests {
  private static readonly Cuboidf Fallback = new(-1f, 0f, -1f, 1f, 16f, 1f);

  [Fact]
  public void Null_node_yields_the_single_fallback_box() {
    var boxes = FillQuads.BoxesFrom(null, Fallback);
    Assert.Single(boxes);
    Assert.Equal(Fallback, boxes[0]);
  }

  [Fact]
  public void Empty_array_yields_the_fallback_box() {
    var node = new JsonObject(JArray.Parse("[]"));
    var boxes = FillQuads.BoxesFrom(node, Fallback);
    Assert.Single(boxes);
    Assert.Equal(Fallback, boxes[0]);
  }

  [Fact]
  public void Populated_node_maps_each_quad_to_a_full_height_box() {
    var node = new JsonObject(
      JArray.Parse(
        "[{\"x1\":2,\"z1\":3,\"x2\":12,\"z2\":13},"
          + "{\"x1\":0,\"z1\":0,\"x2\":4,\"z2\":4}]"
      )
    );

    var boxes = FillQuads.BoxesFrom(node, Fallback);

    Assert.Equal(2, boxes.Length);
    Assert.Equal(new Cuboidf(2f, 0f, 3f, 12f, 16f, 13f), boxes[0]);
    Assert.Equal(new Cuboidf(0f, 0f, 0f, 4f, 16f, 4f), boxes[1]);
  }
}

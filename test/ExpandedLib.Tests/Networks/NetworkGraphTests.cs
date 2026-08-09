using ExpandedLib.Blocks.Networks;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Doubles;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Exercises the graph engine in <see cref="BlockNetworkModSystem"/> - node add/merge, BFS
/// fracture on removal, and root rebuild - using a medium-less <see cref="StubNetwork"/> so only
/// topology behaviour is under test.
/// </summary>
public class NetworkGraphTests {
  private static TestWorld NewWorld() {
    var w = new TestWorld();
    w.RegisterNetwork("test", sys => new StubNetwork(sys));
    return w;
  }

  /// <summary>Places a straight "ns" run along +Z at z=0..count-1 (one shared block instance,
  /// many cells - as the engine does) and registers every cell as a node.</summary>
  private static BlockPos[] BuildLine(TestWorld w, int count) {
    var block = TestNetworkBlock.Create("test", "ns", id: 1);
    var positions = new BlockPos[count];
    for (int z = 0; z < count; z++) {
      var pos = new BlockPos(0, 0, z);
      positions[z] = pos;
      w.Place(pos, block);
    }
    foreach (var pos in positions)
      w.AddNode(pos, "test");
    return positions;
  }

  [Fact]
  public void AddNode_isolated_creates_standalone_network() {
    var w = NewWorld();
    var pos = new BlockPos(0, 0, 0);
    w.Place(pos, TestNetworkBlock.Create("test", "ns", 1));

    w.AddNode(pos, "test");

    var net = w.NetworkAt(pos);
    Assert.NotNull(net);
    Assert.Single(net!.Nodes);
  }

  [Fact]
  public void AddNode_adjacent_same_type_merges_into_one_network() {
    var w = NewWorld();
    var positions = BuildLine(w, 3);

    var net = w.NetworkAt(positions[0]);
    Assert.NotNull(net);
    Assert.Equal(3, net!.Nodes.Count);
    // All cells resolve to the very same network instance.
    Assert.Same(net, w.NetworkAt(positions[1]));
    Assert.Same(net, w.NetworkAt(positions[2]));
  }

  [Fact]
  public void RemoveNode_middle_fractures_into_two_networks() {
    var w = NewWorld();
    var positions = BuildLine(w, 3);

    w.RemoveNode(positions[1]);

    Assert.Null(w.NetworkAt(positions[1]));
    var left = w.NetworkAt(positions[0]);
    var right = w.NetworkAt(positions[2]);
    Assert.NotNull(left);
    Assert.NotNull(right);
    Assert.NotSame(left, right);
    Assert.Single(left!.Nodes);
    Assert.Single(right!.Nodes);
  }

  [Fact]
  public void RemoveNode_end_keeps_remainder_connected() {
    var w = NewWorld();
    var positions = BuildLine(w, 3);

    w.RemoveNode(positions[2]);

    var net = w.NetworkAt(positions[0]);
    Assert.NotNull(net);
    Assert.Equal(2, net!.Nodes.Count);
    Assert.Same(net, w.NetworkAt(positions[1]));
  }

  [Fact]
  public void RebuildFromRoot_preserves_root_network_state() {
    var w = NewWorld();
    var positions = BuildLine(w, 3);
    ((StubNetwork)w.NetworkAt(positions[0])!).Tag = "hot";

    var rebuilt = w.Networks.RebuildFromRoot(w.Accessor, positions[0], "test");

    Assert.NotNull(rebuilt);
    Assert.Equal(3, rebuilt!.Nodes.Count);
    Assert.Equal("hot", ((StubNetwork)rebuilt).Tag);
  }
}

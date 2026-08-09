using ExpandedLib.Blocks.Machines;
using ExpandedLib.Testing;
using ExpandedLib.Testing.Doubles;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The shared machine-port helpers: a fixed machine reads the network in the cell across its
/// connector face, but only when that block presents a connector facing back.
/// </summary>
public class MachinePortsTests {
  private static (TestWorld world, CapturingNode machine) Setup(
    string pipeOrientation
  ) {
    var world = new TestWorld();
    world.RegisterNetwork("test", sys => new StubNetwork(sys));

    // A network node one cell south of the machine.
    var pipePos = new BlockPos(0, 0, 1);
    world.Place(pipePos, TestNetworkBlock.Create("test", pipeOrientation, 1));
    world.AddNode(pipePos, "test");

    // The machine sits at origin and is linked to the world API so it can resolve the manager.
    var machine = new CapturingNode { Pos = new BlockPos(0, 0, 0) };
    world.Attach(machine);
    return (world, machine);
  }

  [Fact]
  public void ConnectedNetwork_returns_the_network_across_a_reciprocal_connector() {
    var (world, machine) = Setup("ns"); // pipe faces north (back at the machine)

    var net = machine.ConnectedNetwork<StubNetwork>(BlockFacing.SOUTH);

    Assert.NotNull(net);
    Assert.Same(world.NetworkAt(new BlockPos(0, 0, 1)), net);
  }

  [Fact]
  public void ConnectedNetwork_is_null_without_a_connector_facing_back() {
    // "we" faces east/west - nothing pointing north at the machine.
    var (_, machine) = Setup("we");

    Assert.Null(machine.ConnectedNetwork<StubNetwork>(BlockFacing.SOUTH));
  }

  [Fact]
  public void ConnectedNetwork_is_null_toward_an_empty_face() {
    var (_, machine) = Setup("ns");
    Assert.Null(machine.ConnectedNetwork<StubNetwork>(BlockFacing.NORTH));
  }

  [Fact]
  public void NetworkAt_resolves_a_cell_directly() {
    var (world, machine) = Setup("ns");
    var pos = new BlockPos(0, 0, 1);
    Assert.Same(world.NetworkAt(pos), machine.NetworkAt<StubNetwork>(pos));
  }
}

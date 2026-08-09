using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The cowper-stove heat sink: a passive block entity that holds the regenerator temperature pushed
/// in by the stove and persists it. The incandescent re-light is a client render concern; this pins
/// the value semantics and the save/reload round trip.
/// </summary>
public class HeatSinkTests {
  private static BlockEntityHeatSink Sink() {
    var world = new TestWorld();
    var be = new BlockEntityHeatSink {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(new Block(), "smex:heatsink", 80),
    };
    world.Attach(be);
    return be;
  }

  [Fact]
  public void Temperature_defaults_to_room_temperature() {
    Assert.Equal(20f, Sink().Temperature);
  }

  [Fact]
  public void Temperature_can_be_pushed_and_read_back() {
    var be = Sink();
    be.Temperature = 850f;
    Assert.Equal(850f, be.Temperature);
  }

  [Fact]
  public void Temperature_round_trips_through_the_tree() {
    var src = Sink();
    src.Temperature = 720f;

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Sink();
    dst.FromTreeAttributes(tree, new TestWorld().World);

    Assert.Equal(720f, dst.Temperature);
  }
}

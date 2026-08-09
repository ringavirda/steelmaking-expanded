using System.Linq;
using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The cowper-stove regenerator's local behaviour that needs no gas network: it pushes its internal
/// core temperature out to the stacked heat sinks each tick, and persists that temperature. The
/// network-fed heat-exchange path lives in the integration suite (it needs a connected exhaust run).
/// </summary>
public class CowperStoveTests {
  private static BlockEntityCowperStove Stove(TestWorld world) {
    var be = new BlockEntityCowperStove {
      Pos = new BlockPos(0, 4, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:cowperstove-north",
        85,
        ("side", "north")
      ),
    };
    world.Attach(be);
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation"); // establish the angle
    return be;
  }

  private static BlockEntityHeatSink PlaceSink(
    TestWorld world,
    BlockEntityCowperStove stove,
    int y
  ) {
    var pos = (BlockPos)
      ReflectionHelpers.Invoke(stove, "GetGlobalPos", 0, y, 1)!;
    var hs = new BlockEntityHeatSink {
      Block = TestBlocks.Configure(new Block(), "smex:heatsink", 80),
    };
    world.Place(pos, hs.Block, hs);
    world.Attach(hs);
    return hs;
  }

  [Fact]
  public void UpdateHeatsinks_pushes_the_core_temperature_into_the_stack() {
    var world = new TestWorld();
    var stove = Stove(world);
    var sinks = new[] { 0, 1, 2, 3 }
      .Select(y => PlaceSink(world, stove, y))
      .ToArray();
    ReflectionHelpers.SetField(stove, "_internalTemperature", 640f);

    ReflectionHelpers.Invoke(stove, "UpdateHeatsinks");

    Assert.All(sinks, hs => Assert.Equal(640f, hs.Temperature));
  }

  [Fact]
  public void Internal_temperature_and_status_round_trip_through_the_tree() {
    var world = new TestWorld();
    var src = Stove(world);
    ReflectionHelpers.SetField(src, "_internalTemperature", 910f);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Stove(world);
    dst.FromTreeAttributes(tree, world.World);

    Assert.Equal(
      910f,
      (float)ReflectionHelpers.GetField(dst, "_internalTemperature")!,
      1
    );
  }
}

using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The blast-furnace tap: an open/closed spout that hands the furnace's molten metal down into the
/// canal start beneath it. Covers the open/closed toggle, persistence, and the <c>TryPourMetal</c>
/// handoff (gated on being open and on a receiving canal start below).
/// </summary>
public class BlastFurnaceTapTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    return world;
  }

  private static BlockEntityBlastFurnaceTap Tap(TestWorld world) {
    var be = new BlockEntityBlastFurnaceTap {
      Pos = new BlockPos(0, 12, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:blastfurnacetap-north",
        1,
        ("side", "north")
      ),
    };
    world.Place(be.Pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  /// <summary>Places a canal start in the cell the tap pours into (Pos + side.Opposite, one down).</summary>
  private static BlockEntityMoltenCanalStart CanalBelow(
    TestWorld world,
    BlockEntityBlastFurnaceTap tap
  ) {
    var facing = BlockFacing.FromCode("north");
    var pos = tap.Pos.AddCopy(facing.Opposite).DownCopy();
    var start = new BlockEntityMoltenCanalStart {
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanalstart-ns",
        2,
        ("type", "start"),
        ("orientation", "ns")
      ),
    };
    world.Place(pos, start.Block, start);
    world.Attach(start);
    return start;
  }

  private static ItemStack IronStack(TestWorld world, int units, float temp) =>
    new(world.World.GetItem(new AssetLocation(Iron))!, units) {
      // Temperature carrier so the canal start's pour path works.
    };

  #region Toggle / persistence

  [Fact]
  public void Defaults_closed_and_toggles_open() {
    var be = Tap(NewWorld());
    Assert.False(be.IsPouring);
    be.TogglePouring();
    Assert.True(be.IsPouring);
    be.TogglePouring();
    Assert.False(be.IsPouring);
  }

  [Fact]
  public void Pour_state_round_trips_through_the_tree() {
    var world = NewWorld();
    var src = Tap(world);
    src.TogglePouring();

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Tap(world);
    dst.FromTreeAttributes(tree, world.World);

    Assert.True(dst.IsPouring);
  }

  #endregion

  #region TryPourMetal

  [Fact]
  public void A_closed_tap_pours_nothing() {
    var world = NewWorld();
    var tap = Tap(world);
    CanalBelow(world, tap); // present, but tap is closed

    int accepted = tap.TryPourMetal(IronStack(world, 20, 1400f), 1400f);

    Assert.Equal(0, accepted);
  }

  [Fact]
  public void An_open_tap_hands_metal_to_the_canal_start_below() {
    var world = NewWorld();
    var tap = Tap(world);
    var canal = CanalBelow(world, tap);
    tap.TogglePouring();

    int accepted = tap.TryPourMetal(IronStack(world, 20, 1400f), 1400f);

    Assert.True(accepted > 0);
    Assert.True(canal.CellAmount > 0);
    Assert.Equal(Iron, canal.CellMetalType);
  }

  [Fact]
  public void An_open_tap_over_nothing_pours_nothing() {
    var world = NewWorld();
    var tap = Tap(world);
    tap.TogglePouring(); // open, but no canal beneath

    Assert.Equal(0, tap.TryPourMetal(IronStack(world, 20, 1400f), 1400f));
  }

  #endregion
}

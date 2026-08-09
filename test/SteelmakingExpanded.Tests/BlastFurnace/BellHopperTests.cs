using ExpandedLib.Testing;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The bell hopper crafts blast mix from the iron/coke/flux in the reinforced hopper above into its
/// internal magazine, then drops it down the furnace shaft. Covers the magazine/dropping persistence,
/// the furnace-full check, and the crafting recipe (consuming the exact feed into the magazine).
/// </summary>
public class BellHopperTests {
  private static BlockEntityHopperBell Bell(TestWorld world, BlockPos pos) {
    var be = new BlockEntityHopperBell {
      Pos = pos,
      Block = TestBlocks.Configure(new Block(), "smex:hopperbell", 90),
    };
    world.Place(pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  private static BlockEntityHopperReinforced HopperAbove(
    TestWorld world,
    BlockPos bellPos
  ) {
    var be = new BlockEntityHopperReinforced {
      Block = TestBlocks.Configure(new Block(), "smex:hopperreinforced", 91),
    };
    var pos = bellPos.UpCopy();
    world.Place(pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  private static void Put(InventoryBase inv, int slot, string code, int count) {
    var item = new Item { Code = new AssetLocation(code) };
    inv[slot].Itemstack = new ItemStack(item, count);
  }

  #region Default state

  [Fact]
  public void A_freshly_placed_bell_hopper_is_dropping_by_default() {
    // Dropping is on out of the box so a newly built furnace feeds itself without the
    // player first discovering the Ctrl + right-click toggle.
    var bell = Bell(new TestWorld(), new BlockPos(0, 16, 0));
    Assert.True(bell.IsDropping);
  }

  [Fact]
  public void Dropping_defaults_on_when_a_saved_tree_omits_the_flag() {
    var world = new TestWorld();
    var bell = Bell(world, new BlockPos(0, 16, 0));
    ReflectionHelpers.SetField(bell, "_isDropping", false);

    bell.FromTreeAttributes(new TreeAttribute(), world.World); // legacy tree, no key

    Assert.True(bell.IsDropping);
  }

  [Fact]
  public void An_explicitly_stopped_bell_stays_stopped_across_a_reload() {
    // The new default must not override a hopper the player deliberately switched off.
    var world = new TestWorld();
    var src = Bell(world, new BlockPos(0, 16, 0));
    src.IsDropping = false;

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Bell(world, new BlockPos(0, 16, 0));
    dst.FromTreeAttributes(tree, world.World);

    Assert.False(dst.IsDropping);
  }

  #endregion

  #region Persistence

  [Fact]
  public void Magazine_and_dropping_round_trip_through_the_tree() {
    var world = new TestWorld();
    var src = Bell(world, new BlockPos(0, 16, 0));
    ReflectionHelpers.SetField(src, "_blastMixMagazine", 24);
    src.IsDropping = true;

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Bell(world, new BlockPos(0, 16, 0));
    dst.FromTreeAttributes(tree, world.World);

    Assert.Equal(24, dst.BlastMixMagazine);
    Assert.True(dst.IsDropping);
  }

  #endregion

  #region Furnace-full check

  [Fact]
  public void IsFurnaceFull_is_false_with_no_coalpile_below() {
    var world = new TestWorld();
    Assert.False(Bell(world, new BlockPos(0, 16, 0)).IsFurnaceFull());
  }

  #endregion

  #region Crafting

  [Fact]
  public void OnServerTick_crafts_blastmix_from_a_full_hopper_into_the_magazine() {
    var world = new TestWorld();
    var bellPos = new BlockPos(0, 16, 0);
    var bell = Bell(world, bellPos);
    var hopper = HopperAbove(world, bellPos);

    // Exactly one recipe's worth of feed: 12 iron + 3 coke + 1 lime -> 16 blastmix.
    Put(
      hopper.Inventory,
      0,
      "game:crushed-iron",
      SmexValues.HopperIronOreRequired
    );
    Put(hopper.Inventory, 2, "game:coke", SmexValues.HopperCokeRequired);
    Put(hopper.Inventory, 3, "game:lime", SmexValues.HopperLimeRequired);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBlastmixProduced, bell.BlastMixMagazine);
    Assert.True(hopper.Inventory[0].Empty); // iron consumed
    Assert.True(hopper.Inventory[2].Empty); // coke consumed
    Assert.True(hopper.Inventory[3].Empty); // lime consumed
  }

  [Fact]
  public void OnServerTick_crafts_nothing_without_enough_feed() {
    var world = new TestWorld();
    var bellPos = new BlockPos(0, 16, 0);
    var bell = Bell(world, bellPos);
    var hopper = HopperAbove(world, bellPos);

    // Iron + coke but no flux -> recipe can't complete.
    Put(
      hopper.Inventory,
      0,
      "game:crushed-iron",
      SmexValues.HopperIronOreRequired
    );
    Put(hopper.Inventory, 2, "game:coke", SmexValues.HopperCokeRequired);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(0, bell.BlastMixMagazine);
    Assert.False(hopper.Inventory[0].Empty); // nothing consumed
  }

  [Fact]
  public void OnServerTick_reclaims_loose_blastmix_into_the_magazine() {
    var world = new TestWorld();
    var bellPos = new BlockPos(0, 16, 0);
    var bell = Bell(world, bellPos);
    var hopper = HopperAbove(world, bellPos);

    // Reclaimed blastmix sitting in an iron slot feeds 1:1 into the magazine.
    Put(hopper.Inventory, 0, "smex:blastmix", 8);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(8, bell.BlastMixMagazine);
    Assert.True(hopper.Inventory[0].Empty);
  }

  #endregion
}

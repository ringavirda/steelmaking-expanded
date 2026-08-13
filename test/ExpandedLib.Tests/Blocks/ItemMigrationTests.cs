using System.Collections.Generic;
using ExpandedLib.Blocks.Migrations;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Inventory stacks are routed by <see cref="EnumItemClass"/>: item stacks through the item remap
/// table, block stacks through the block one. The two tables are keyed by <see cref="AssetLocation"/>
/// and an item and a block may legitimately carry the same code, so a single shared table would let
/// one kind rewrite - or, when nothing resolves on the other side, silently delete - the other.
/// </summary>
public class ItemMigrationTests {
  #region Fixture

  private static readonly AssetLocation Shared = new("smex", "sharedcode");

  private static (BlockMigrationModSystem sys, InventoryGeneric inv) Rig(
    TestWorld world,
    ItemStack stack,
    Item replacement
  ) {
    var sys = new BlockMigrationModSystem();

    // The table the sweep would have built from an IItemCodeMigration; the build itself needs a
    // server API, so seed it directly and exercise the routing.
    var table =
      (Dictionary<AssetLocation, Item>)
        ReflectionHelpers.GetField(sys, "_itemRemap")!;
    table[Shared] = replacement;

    var inv = new InventoryGeneric(1, "test", "test", world.Api, null);
    inv[0].Itemstack = stack;
    return (sys, inv);
  }

  private static int Remap(BlockMigrationModSystem sys, InventoryGeneric inv) =>
    (int)ReflectionHelpers.Invoke(sys, "RemapInventory", inv)!;

  #endregion

  #region Routing by stack class

  [Fact]
  public void An_item_stack_is_rewritten_through_the_item_table() {
    var world = new TestWorld();
    var old = new Item { Code = Shared, ItemId = 1 };
    var replacement = new Item {
      Code = new AssetLocation("game", "coke"),
      ItemId = 2,
    };
    var (sys, inv) = Rig(world, new ItemStack(old, 7), replacement);

    int changed = Remap(sys, inv);

    Assert.Equal(1, changed);
    Assert.Equal(replacement.Code, inv[0].Itemstack!.Collectible.Code);
    Assert.Equal(7, inv[0].Itemstack!.StackSize); // stack size carries over
  }

  [Fact]
  public void A_block_stack_is_left_alone_by_the_item_table() {
    var world = new TestWorld();
    var block = TestBlocks.Configure(new Block(), Shared.ToString(), 40);
    var replacement = new Item {
      Code = new AssetLocation("game", "coke"),
      ItemId = 2,
    };
    var (sys, inv) = Rig(world, new ItemStack(block, 3), replacement);

    int changed = Remap(sys, inv);

    // Same code, different kind: routing it through the item table would turn a placeable block
    // into an item, and the block table would have deleted it as an unresolvable purge.
    Assert.Equal(0, changed);
    Assert.Equal(Shared, inv[0].Itemstack!.Collectible.Code);
    Assert.Equal(EnumItemClass.Block, inv[0].Itemstack!.Class);
  }

  #endregion

  #region Stacks lying on the ground

  // A legacy stack a player threw away, or one a broken container scattered, is an entity rather
  // than a block or an inventory slot, so neither the voxel loop nor the container sweep reaches it.
  // Once the old item's asset is gone that stack is an unusable "unknown item" for good.

  [Fact]
  public void A_dropped_stack_is_rewritten_by_the_ground_sweep() {
    var world = new TestWorld();
    var old = new Item { Code = Shared, ItemId = 1 };
    var replacement = new Item {
      Code = new AssetLocation("smex", "burden"),
      ItemId = 2,
    };
    var (sys, _) = Rig(world, new ItemStack(old, 4), replacement);

    var dropped = new EntityItem { Itemstack = new ItemStack(old, 4) };
    int changed = (int)
      ReflectionHelpers.Invoke(sys, "RemapGroundItems", Chunk(dropped))!;

    Assert.Equal(1, changed);
    Assert.Equal(replacement.Code, dropped.Itemstack.Collectible.Code);
    Assert.Equal(4, dropped.Itemstack.StackSize);
  }

  [Fact]
  public void An_unmapped_dropped_stack_is_left_alone() {
    var world = new TestWorld();
    var other = new Item {
      Code = new AssetLocation("smex", "unrelated"),
      ItemId = 9,
    };
    var replacement = new Item {
      Code = new AssetLocation("smex", "burden"),
      ItemId = 2,
    };
    var (sys, _) = Rig(world, new ItemStack(other, 1), replacement);

    var dropped = new EntityItem { Itemstack = new ItemStack(other, 3) };

    Assert.Equal(
      0,
      (int)ReflectionHelpers.Invoke(sys, "RemapGroundItems", Chunk(dropped))!
    );
    Assert.Equal(other.Code, dropped.Itemstack.Collectible.Code);
  }

  [Fact]
  public void Entities_past_the_live_count_are_not_touched() {
    // IWorldChunk.Entities is allocated larger than the live entity count, so a sweep that trusted
    // the array length would read stale slots left behind by despawned entities.
    var world = new TestWorld();
    var old = new Item { Code = Shared, ItemId = 1 };
    var replacement = new Item {
      Code = new AssetLocation("smex", "burden"),
      ItemId = 2,
    };
    var (sys, _) = Rig(world, new ItemStack(old, 1), replacement);

    var stale = new EntityItem { Itemstack = new ItemStack(old, 1) };
    var chunk = Substitute.For<IWorldChunk>();
    chunk.Entities.Returns([stale]);
    chunk.EntitiesCount.Returns(0); // the slot is stale, not live

    Assert.Equal(
      0,
      (int)ReflectionHelpers.Invoke(sys, "RemapGroundItems", chunk)!
    );
    Assert.Equal(Shared, stale.Itemstack.Collectible.Code);
  }

  private static IWorldChunk Chunk(params Entity[] entities) {
    var chunk = Substitute.For<IWorldChunk>();
    chunk.Entities.Returns(entities);
    chunk.EntitiesCount.Returns(entities.Length);
    return chunk;
  }

  #endregion

  #region Routing by stack class (continued)

  [Fact]
  public void An_unmapped_item_stack_is_left_alone() {
    var world = new TestWorld();
    var other = new Item {
      Code = new AssetLocation("smex", "unrelated"),
      ItemId = 9,
    };
    var replacement = new Item {
      Code = new AssetLocation("game", "coke"),
      ItemId = 2,
    };
    var (sys, inv) = Rig(world, new ItemStack(other, 5), replacement);

    Assert.Equal(0, Remap(sys, inv));
    Assert.Equal(other.Code, inv[0].Itemstack!.Collectible.Code);
  }

  #endregion
}

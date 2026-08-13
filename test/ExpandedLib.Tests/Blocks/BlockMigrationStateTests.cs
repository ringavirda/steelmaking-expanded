using System;
using System.Collections.Generic;
using System.Reflection;
using ExpandedLib.Blocks.Migrations;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// A block-code migration swaps the block through <c>SetBlock</c>, which the engine documents as
/// running OnBlockRemoved/OnBlockPlaced - so the block entity at that cell is destroyed and a fresh
/// one placed. Everything a machine had learned (a heat sink's temperature, a furnace tap's state, a
/// pipe's network bindings) lives in that entity, so a migration that does not carry it across resets
/// every migrated machine in the world on the first load after an update.
/// </summary>
public class BlockMigrationStateTests {
  #region Fixture

  private const string EntityClass = "test.stateful";
  private static readonly AssetLocation OldCode = new("smex", "widget");
  private static readonly AssetLocation NewCode = new("smex", "widget-tier3");

  /// <summary>A block entity with one piece of state that only survives if the tree is carried over.</summary>
  private sealed class StatefulBe : BlockEntity {
    public float Charge;

    public override void ToTreeAttributes(ITreeAttribute tree) {
      base.ToTreeAttributes(tree);
      tree.SetFloat("charge", Charge);
    }

    public override void FromTreeAttributes(
      ITreeAttribute tree,
      IWorldAccessor world
    ) {
      base.FromTreeAttributes(tree, world);
      Charge = tree.GetFloat("charge");
    }
  }

  /// <summary>A migration that only renames the code, declaring no block-entity handling.</summary>
  private sealed class PlainRename : IBlockCodeMigration {
    public string Name => "test plain rename";

    public IEnumerable<(
      AssetLocation oldCode,
      AssetLocation newCode
    )> GetRemaps(Vintagestory.API.Server.ICoreServerAPI api) => [];
  }

  private static (TestWorld world, BlockPos pos, Block newBlock) Rig(
    float charge
  ) {
    var world = new TestWorld();
    world.RegisterBlockEntityFactory(EntityClass, () => new StatefulBe());

    Block oldBlock = TestBlocks.Configure(new Block(), OldCode.ToString(), 40);
    Block newBlock = TestBlocks.Configure(new Block(), NewCode.ToString(), 41);
    oldBlock.EntityClass = EntityClass;
    newBlock.EntityClass = EntityClass;

    var pos = new BlockPos(4, 5, 6);
    world.Place(pos, oldBlock, new StatefulBe { Charge = charge });
    world.Register(newBlock);
    return (world, pos, newBlock);
  }

  private static void Replace(TestWorld world, BlockPos pos, Block newBlock) {
    var sys = new BlockMigrationModSystem();
    // ReplaceBlock resolves the world through the server API the mod system is started with.
    ReflectionHelpers.SetField(sys, "_sapi", world.Api);
    ReflectionHelpers.Invoke(
      sys,
      "ReplaceBlock",
      world.Accessor,
      pos,
      RemapEntryFor(newBlock)
    );
  }

  // RemapEntry is a private nested record struct; build one positionally.
  private static object RemapEntryFor(Block newBlock) {
    Type entry = typeof(BlockMigrationModSystem).GetNestedType(
      "RemapEntry",
      BindingFlags.NonPublic
    )!;
    return Activator.CreateInstance(
      entry,
      newBlock,
      OldCode,
      NewCode,
      null
    )!;
  }

  #endregion

  #region The premise

  [Fact]
  public void SetBlock_replaces_the_block_entity() {
    // Guards the fixture itself: if SetBlock ever stopped destroying the entity, every test below
    // would pass whether or not the migration carried state, so assert the hazard is really present.
    var (world, pos, _) = Rig(charge: 42f);
    var original = world.GetBlockEntity(pos);

    world.Accessor.SetBlock(41, pos);

    var after = world.GetBlockEntity(pos);
    Assert.NotNull(after);
    Assert.NotSame(original, after);
    Assert.Equal(0f, ((StatefulBe)after!).Charge);
  }

  #endregion

  #region Carrying state across a plain migration

  [Fact]
  public void A_plain_migration_carries_the_block_entity_state_across() {
    var (world, pos, newBlock) = Rig(charge: 42f);

    Replace(world, pos, newBlock);

    var migrated = Assert.IsType<StatefulBe>(world.GetBlockEntity(pos));
    Assert.Equal(NewCode.ToString(), world.GetBlock(pos).Code.ToString());
    Assert.Equal(42f, migrated.Charge);
  }

  [Fact]
  public void The_migrated_cell_keeps_its_position() {
    var (world, pos, newBlock) = Rig(charge: 7f);

    Replace(world, pos, newBlock);

    Assert.Equal(pos, world.GetBlockEntity(pos)!.Pos);
  }

  #endregion
}

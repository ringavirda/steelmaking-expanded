using ExpandedLib.Blocks.Healing;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The orphaned-block-entity healer: a block still placed in the world but missing its block entity
/// (the engine discards a BE whose deserialization throws, or a server desync drops it) gets a fresh
/// one recreated, while a healthy block and a block that never carries a BE are both left alone.
/// Mirrors the player-reported blast-furnace door that went inert after its
/// <c>BlockEntityBlastFurnace</c> was lost from the save.
/// </summary>
public class BlockEntityHealTests {
  private const string TestBeClass = "healtest";

  #region Healing

  [Fact]
  public void An_orphaned_block_with_no_block_entity_is_healed() {
    var world = new TestWorld();
    var pos = new BlockPos(10, 20, 30, 0);

    // A placed block that declares a block entity, but with none attached - the orphan state.
    world.Place(pos, MakeBlockEntityBlock("exlib:healtest", 4242));
    world.RegisterBlockEntityFactory(TestBeClass, () => new ProbeBlockEntity());
    Assert.Null(world.GetBlockEntity(pos));

    bool healed = Healer(world).HealOrphanAt(world.Accessor, pos);

    Assert.True(healed);
    var be = Assert.IsType<ProbeBlockEntity>(world.GetBlockEntity(pos));
    Assert.True(
      be.WasInitialized,
      "the recreated block entity should be initialized"
    );
  }

  [Fact]
  public void A_block_that_still_has_its_block_entity_is_left_untouched() {
    var world = new TestWorld();
    var pos = new BlockPos(1, 2, 3, 0);

    var existing = new ProbeBlockEntity();
    world.Place(pos, MakeBlockEntityBlock("exlib:healtest", 4242), existing);
    world.RegisterBlockEntityFactory(TestBeClass, () => new ProbeBlockEntity());

    bool healed = Healer(world).HealOrphanAt(world.Accessor, pos);

    Assert.False(healed);
    // The original entity is kept, not replaced with a fresh default one.
    Assert.Same(existing, world.GetBlockEntity(pos));
  }

  [Fact]
  public void A_block_without_a_block_entity_class_is_ignored() {
    var world = new TestWorld();
    var pos = new BlockPos(4, 5, 6, 0);

    // No EntityClass: a plain block that should never carry a BE - nothing to heal.
    world.Place(pos, TestBlocks.Configure(new Block(), "exlib:plain", 4243));

    bool healed = Healer(world).HealOrphanAt(world.Accessor, pos);

    Assert.False(healed);
    Assert.Null(world.GetBlockEntity(pos));
  }

  #endregion

  #region Helpers

  private static Block MakeBlockEntityBlock(string code, int id) {
    var block = TestBlocks.Configure(new Block(), code, id);
    block.EntityClass = TestBeClass;
    return block;
  }

  private static BlockEntityHealModSystem Healer(TestWorld world) {
    var healer = new BlockEntityHealModSystem();
    // The system captures the server API in StartServerSide, which the headless harness never runs.
    ReflectionHelpers.SetField(healer, "_sapi", world.Api);
    return healer;
  }

  /// <summary>Minimal block entity that records whether it was initialized after spawning.</summary>
  private sealed class ProbeBlockEntity : BlockEntity {
    public bool WasInitialized { get; private set; }

    public override void Initialize(ICoreAPI api) {
      Api = api;
      WasInitialized = true;
    }
  }

  #endregion
}

using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The blast furnace's firing/melting state machine is gated on a full multiblock with live tuyeres,
/// gas outlets and hearth piles - too much to fake wholesale - but the state persistence, the molten
/// stack construction, and the simple state transitions (transition to melting, extinguish-to-idle)
/// stand on their own. Those are pinned here.
/// </summary>
public class BlastFurnaceTests {
  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem("game:ingot-iron", 1500f);
    world.RegisterItem("smex:slag");
    return world;
  }

  private static BlockEntityBlastFurnace Furnace(TestWorld world) {
    var be = new BlockEntityBlastFurnace {
      Pos = new BlockPos(0, 16, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:blastfurnacedoor-north",
        1,
        ("side", "north")
      ),
      BaseAngleRad = 0f,
    };
    world.Attach(be);
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation");
    ReflectionHelpers.Invoke(be, "CacheAttributes");
    return be;
  }

  #region State machine

  [Fact]
  public void Defaults_to_idle() {
    Assert.Equal(BlastFurnaceState.Idle, Furnace(NewWorld()).State);
  }

  [Fact]
  public void TransitionToMelting_moves_firing_into_melting() {
    var be = Furnace(NewWorld());
    ReflectionHelpers.SetProperty(
      be,
      nameof(be.State),
      BlastFurnaceState.Firing
    );

    ReflectionHelpers.Invoke(be, "TransitionToMelting");

    Assert.Equal(BlastFurnaceState.Melting, be.State);
    Assert.Equal(0f, (float)ReflectionHelpers.GetField(be, "_meltSeconds")!, 3);
  }

  [Fact]
  public void Extinguish_returns_to_idle_and_resets_heat() {
    var be = Furnace(NewWorld());
    ReflectionHelpers.SetProperty(
      be,
      nameof(be.State),
      BlastFurnaceState.Melting
    );
    ReflectionHelpers.SetField(be, "_internalTemp", 1500f);
    // No molten iron, so the solidified-iron drop branch is skipped.

    ReflectionHelpers.Invoke(be, "Extinguish");

    Assert.Equal(BlastFurnaceState.Idle, be.State);
    Assert.Equal(
      20f,
      (float)ReflectionHelpers.GetField(be, "_internalTemp")!,
      1
    );
    Assert.Equal(0f, (float)ReflectionHelpers.GetField(be, "_moltenIron")!, 3);
  }

  #endregion

  #region Molten stack construction

  [Fact]
  public void CreateMoltenStack_builds_iron_at_the_network_code() {
    var world = NewWorld();
    var be = Furnace(world);

    var stack = (ItemStack?)
      ReflectionHelpers.Invoke(be, "CreateMoltenStack", "iron", 12, 1400f);

    Assert.NotNull(stack);
    Assert.Equal("game:ingot-iron", stack!.Collectible.Code.ToString());
    Assert.Equal(12, stack.StackSize);
  }

  [Fact]
  public void CreateMoltenStack_maps_slag_to_its_own_domain() {
    var world = NewWorld();
    var be = Furnace(world);

    var stack = (ItemStack?)
      ReflectionHelpers.Invoke(be, "CreateMoltenStack", "slag", 8, 1300f);

    Assert.NotNull(stack);
    Assert.Equal("smex:slag", stack!.Collectible.Code.ToString());
  }

  [Fact]
  public void CreateMoltenStack_is_null_for_an_unresolved_metal() {
    var world = NewWorld(); // gold not registered
    var be = Furnace(world);

    Assert.Null(
      (ItemStack?)
        ReflectionHelpers.Invoke(be, "CreateMoltenStack", "gold", 5, 1400f)
    );
  }

  #endregion

  #region Serialization

  [Fact]
  public void Furnace_state_round_trips_through_the_tree() {
    var world = NewWorld();
    var src = Furnace(world);
    ReflectionHelpers.SetProperty(
      src,
      nameof(src.State),
      BlastFurnaceState.Melting
    );
    ReflectionHelpers.SetProperty(src, nameof(src.IsChoked), true);
    ReflectionHelpers.SetField(src, "_internalTemp", 1456f);
    ReflectionHelpers.SetField(src, "_moltenIron", 80f);
    ReflectionHelpers.SetField(src, "_moltenSlag", 40f);
    ReflectionHelpers.SetField(src, "_cachedMixCount", 220);
    ReflectionHelpers.SetField(src, "_cachedIsFull", true);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Furnace(world);
    dst.FromTreeAttributes(tree, world.World);

    Assert.Equal(BlastFurnaceState.Melting, dst.State);
    Assert.True(dst.IsChoked);
    Assert.Equal(
      1456f,
      (float)ReflectionHelpers.GetField(dst, "_internalTemp")!,
      1
    );
    Assert.Equal(
      80f,
      (float)ReflectionHelpers.GetField(dst, "_moltenIron")!,
      1
    );
    Assert.Equal(
      40f,
      (float)ReflectionHelpers.GetField(dst, "_moltenSlag")!,
      1
    );
    Assert.Equal(220, (int)ReflectionHelpers.GetField(dst, "_cachedMixCount")!);
  }

  #endregion
}

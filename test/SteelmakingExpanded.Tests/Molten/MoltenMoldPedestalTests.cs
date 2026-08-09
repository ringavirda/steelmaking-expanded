using System;
using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The mold pedestal: a canal fitting that holds one tool mold and drains the run's metal into it
/// each tick until the mold is full or its cast has hardened. Covers the capacity, the pouring gate
/// (a closed pedestal severs the run), mold attach/detach content round-tripping, and the per-tick
/// drain (capacity-, type- and hardened-gated).
/// </summary>
public class MoltenMoldPedestalTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem("game:ingot-copper", 1084f);
    return world;
  }

  private static BlockEntityMoltenCanalMoldPedestal Pedestal(TestWorld world) {
    var be = new BlockEntityMoltenCanalMoldPedestal {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanalmoldpedestal-ns",
        4,
        ("type", "moldpedestal"),
        ("orientation", "ns")
      ),
    };
    world.Attach(be);
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  /// <summary>A tool-mold item stack optionally carrying cast metal in its block-entity attributes.</summary>
  private static ItemStack Mold(
    TestWorld world,
    ItemStack? content = null,
    int units = 0
  ) {
    var block = TestBlocks.Configure(new Block(), "smex:toolmold-anvil", 60);
    world.Register(block);
    var stack = new ItemStack(block);
    if (content != null)
      MoltenContents.Write(stack, MoltenContents.MoldUnitsKey, content, units);
    return stack;
  }

  private static void ServerTick(BlockEntityMoltenCanalMoldPedestal be) =>
    ReflectionHelpers.Invoke(be, "OnServerTick", 1f);

  #region Disabled-mold purge

  [Fact]
  public void A_disabled_mold_is_cleared_from_the_pedestal_on_the_next_tick() {
    var world = NewWorld();
    var be = Pedestal(world);

    var moldBlock = TestBlocks.Configure(
      new Block(),
      "smex:toolmold-blue-fired-plate",
      61
    );
    world.Register(moldBlock);
    be.AddMold(new ItemStack(moldBlock));
    Assert.True(be.IsMold);

    try {
      SteelmakingExpanded.Molds.MoldGating.SetEnabled("plate", false);
      ServerTick(be); // the pedestal purges its own mold on load (chunk scan can't reach it)

      Assert.False(be.IsMold);
      Assert.Null(be.MoldStack);
    } finally {
      SteelmakingExpanded.Molds.MoldGating.SetEnabled("plate", true);
    }
  }

  [Fact]
  public void An_enabled_mold_is_left_on_the_pedestal() {
    var world = NewWorld();
    var be = Pedestal(world);
    be.AddMold(Mold(world)); // anvil mold, never a gated type
    Assert.True(be.IsMold);

    ServerTick(be);

    Assert.True(be.IsMold);
  }

  #endregion

  #region Capacity / connectivity

  [Fact]
  public void Pedestal_capacity_is_half_the_default_rounded_up() {
    var world = NewWorld();
    Assert.Equal(
      (int)Math.Ceiling(SmexValues.CanalDefaultUnitCapacity / 2.0),
      Pedestal(world).MaxUnitCapacity
    );
  }

  [Fact]
  public void An_open_pedestal_passes_metal_a_closed_one_severs() {
    var world = NewWorld();
    var be = Pedestal(world);

    // Pedestal defaults to pouring (open).
    Assert.True(be.IsPouring);
    Assert.False(be.IsConnectionBroken());

    be.TryTogglePouring();
    Assert.False(be.IsPouring);
    Assert.True(be.IsConnectionBroken());
  }

  #endregion

  #region Mold attach / detach

  [Fact]
  public void AddMold_adopts_the_mold_and_its_cast_metal() {
    var world = NewWorld();
    var be = Pedestal(world);

    be.AddMold(Mold(world, Metal(world, Iron, 1200f), units: 10));

    Assert.True(be.IsMold);
    Assert.Equal(10, be.MoldCurrentUnits);
    Assert.NotNull(be.MoldStack);
    Assert.NotNull(be.MoldMetalContent);
  }

  [Fact]
  public void RemoveMold_returns_a_stack_preserving_the_cast() {
    var world = NewWorld();
    var be = Pedestal(world);
    be.AddMold(Mold(world, Metal(world, Iron, 1200f), units: 10));

    var removed = be.RemoveMold();

    Assert.False(be.IsMold);
    Assert.Null(be.MoldStack);
    Assert.Equal(0, be.MoldCurrentUnits);
    var (content, units) = MoltenContents.Read(
      removed,
      MoltenContents.MoldUnitsKey,
      world.World
    );
    Assert.Equal(10, units);
    Assert.NotNull(content);
  }

  #endregion

  #region Server-side drain

  [Fact]
  public void OnServerTick_drains_cell_metal_into_an_empty_mold() {
    var world = NewWorld();
    var be = Pedestal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    be.IsMold = true;

    int expected = Math.Min(20, be.MoldMaxUnits);
    ServerTick(be);

    Assert.Equal(expected, be.MoldCurrentUnits);
    Assert.Equal(20 - expected, be.CellAmount);
    Assert.NotNull(be.MoldMetalContent);
  }

  [Fact]
  public void OnServerTick_does_nothing_without_a_mold() {
    var world = NewWorld();
    var be = Pedestal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    // IsMold stays false

    ServerTick(be);

    Assert.Equal(0, be.MoldCurrentUnits);
    Assert.Equal(20, be.CellAmount);
  }

  [Fact]
  public void OnServerTick_will_not_pour_a_different_metal_into_the_mold() {
    var world = NewWorld();
    var be = Pedestal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    be.IsMold = true;
    ReflectionHelpers.SetProperty(
      be,
      nameof(be.MoldMetalContent),
      Metal(world, "game:ingot-copper", 1100f)
    );
    ReflectionHelpers.SetProperty(be, nameof(be.MoldCurrentUnits), 5);

    ServerTick(be);

    Assert.Equal(5, be.MoldCurrentUnits);
    Assert.Equal(20, be.CellAmount);
  }

  [Fact]
  public void OnServerTick_stops_once_the_cast_has_hardened() {
    var world = NewWorld();
    var be = Pedestal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    be.IsMold = true;
    // A hardened iron cast already in the mold (300 C << 0.3 * 1500).
    ReflectionHelpers.SetProperty(
      be,
      nameof(be.MoldMetalContent),
      Metal(world, Iron, 300f)
    );
    ReflectionHelpers.SetProperty(be, nameof(be.MoldCurrentUnits), 5);

    ServerTick(be);

    Assert.Equal(5, be.MoldCurrentUnits); // a finished cast does not take more
    Assert.Equal(20, be.CellAmount);
  }

  #endregion

  #region Cell solidify + chisel-clear

  // The pedestal's own cell now clogs and is chiselled clear like a canal or the start block, instead
  // of staying permanently liquid - so a run that goes cold with metal left in the cell is recoverable.
  [Fact]
  public void A_cold_pedestal_cell_solidifies_severs_and_can_be_cleared() {
    var world = NewWorld();
    world.RegisterItem("game:metalbit-iron"); // the chiselled-out solid drop
    var be = Pedestal(world);
    be.PushMetal(20, Metal(world, Iron, 400f), world.World); // below the 1500 melting point

    ReflectionHelpers.Invoke(be, "UpdateThermal", world.World);

    Assert.True(be.Solidified);
    Assert.True(be.IsConnectionBroken()); // clogged -> drops off the run

    Assert.NotNull(be.ClearSolidified()); // chiselled clear
    Assert.False(be.Solidified);
  }

  #endregion

  #region Serialization

  [Fact]
  public void Pedestal_state_round_trips_through_the_tree() {
    var world = NewWorld();
    var src = Pedestal(world);
    src.AddMold(Mold(world, Metal(world, Iron, 1200f), units: 8));
    src.TryTogglePouring(); // close it

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var restored = Pedestal(world);
    restored.FromTreeAttributes(tree, world.World);

    Assert.True(restored.IsMold);
    Assert.False(restored.IsPouring);
    Assert.Equal(8, restored.MoldCurrentUnits);
    Assert.NotNull(restored.MoldMetalContent);
  }

  #endregion
}

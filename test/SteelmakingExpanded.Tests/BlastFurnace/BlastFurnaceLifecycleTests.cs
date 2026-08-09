using System.Collections.Generic;
using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The blast furnace's melting math, driven directly: the per-cycle conversion of hearth burden
/// into molten iron + slag (capacity-clamped) and the hearth burden accounting. These are the
/// numbers the firing/melting tick relies on but the gated <c>OnProductionTick</c> made unreachable.
/// </summary>
public class BlastFurnaceLifecycleTests {
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

  /// <summary>A hearth coal pile holding <paramref name="units"/> of burden, lit.</summary>
  private static BlockEntityCoalPile BurdenPile(
    TestWorld world,
    BlockPos pos,
    int units
  ) {
    var pile = new BlockEntityCoalPile { Pos = pos.Copy() };
    // Pass a real Api so slot.MarkDirty() (DidModifyItemSlot) doesn't NRE when ConsumeForMelting
    // takes burden out of the slot.
    var inv = new InventoryGeneric(1, "coalpile", "test", world.Api, null);
    var burden = new Item {
      Code = new AssetLocation("smex", "burden"),
      ItemId = 4242,
    };
    inv[0].Itemstack = new ItemStack(burden, units);
    ReflectionHelpers.SetField(pile, "inventory", inv);
    ReflectionHelpers.SetField(pile, "burning", true);
    world.Place(
      pos,
      TestBlocks.Configure(new Block(), "game:coalpile", 50),
      pile
    );
    world.Attach(pile);
    return pile;
  }

  private static List<(BlockPos, BlockEntityCoalPile)> Piles(
    params (BlockPos, BlockEntityCoalPile)[] p
  ) => new(p);

  private static int Burden(BlockEntityCoalPile pile) =>
    pile.inventory[0].StackSize;

  private static float Iron(BlockEntityBlastFurnace be) =>
    (float)ReflectionHelpers.GetField(be, "_moltenIron")!;

  private static float Slag(BlockEntityBlastFurnace be) =>
    (float)ReflectionHelpers.GetField(be, "_moltenSlag")!;

  #region ConsumeForMelting

  [Fact]
  public void A_melt_cycle_burns_burden_into_molten_iron_and_slag() {
    var world = NewWorld();
    var be = Furnace(world);
    var pile = BurdenPile(world, new BlockPos(0, 13, 0), 100);

    ReflectionHelpers.Invoke(
      be,
      "ConsumeForMelting",
      Piles((pile.Pos, pile)),
      16,
      60f,
      10f
    );

    Assert.Equal(84, Burden(pile)); // 16 burden consumed
    Assert.Equal(60f, Iron(be), 3); // one cycle of iron
    Assert.Equal(10f, Slag(be), 3); // one cycle of slag
  }

  [Fact]
  public void Molten_output_is_clamped_at_the_furnace_capacity() {
    var world = NewWorld();
    var be = Furnace(world);
    var pile = BurdenPile(world, new BlockPos(0, 13, 0), 100);
    // Already near the iron ceiling (2400 default).
    ReflectionHelpers.SetField(
      be,
      "_moltenIron",
      SmexValues.BfMaxMoltenIron - 10f
    );

    ReflectionHelpers.Invoke(
      be,
      "ConsumeForMelting",
      Piles((pile.Pos, pile)),
      16,
      60f,
      10f
    );

    Assert.Equal(SmexValues.BfMaxMoltenIron, Iron(be), 1); // capped, not 2450
  }

  [Fact]
  public void Melting_draws_from_the_upper_piles_first() {
    var world = NewWorld();
    var be = Furnace(world);
    var low = BurdenPile(world, new BlockPos(0, 12, 0), 50);
    var high = BurdenPile(world, new BlockPos(0, 14, 0), 50);

    ReflectionHelpers.Invoke(
      be,
      "ConsumeForMelting",
      Piles((low.Pos, low), (high.Pos, high)),
      16,
      60f,
      10f
    );

    Assert.Equal(34, Burden(high)); // the higher pile is drained first
    Assert.Equal(50, Burden(low)); // the lower pile is untouched
  }

  #endregion

  #region Burden accounting

  [Fact]
  public void Mix_count_totals_the_hearth_and_reports_full_at_the_fire_threshold() {
    var world = NewWorld();
    var be = Furnace(world);
    var pile = BurdenPile(
      world,
      new BlockPos(0, 13, 0),
      SmexValues.BurdenRequiredToFire
    );

    object[] args = { Piles((pile.Pos, pile)), false };
    int count = (int)ReflectionHelpers.Invoke(be, "GetBurdenCount", args)!;

    Assert.Equal(SmexValues.BurdenRequiredToFire, count);
    Assert.True((bool)args[1], "a hearth at the threshold should read as full"); // out isFull
  }

  [Fact]
  public void A_thin_charge_does_not_read_as_full() {
    var world = NewWorld();
    var be = Furnace(world);
    var pile = BurdenPile(world, new BlockPos(0, 13, 0), 10);

    object[] args = { Piles((pile.Pos, pile)), true };
    int count = (int)ReflectionHelpers.Invoke(be, "GetBurdenCount", args)!;

    Assert.Equal(10, count);
    Assert.False((bool)args[1]);
  }

  #endregion

  #region Extinguish solidifies the molten charge

  [Fact]
  public void Extinguishing_a_melt_solidifies_the_iron_in_the_hearth() {
    var world = NewWorld();
    world.Register(
      TestBlocks.Configure(new Block(), "smex:solidifiediron", 70)
    );
    var be = Furnace(world); // no hearth piles -> the slag-conversion walk is a no-op
    ReflectionHelpers.SetProperty(
      be,
      nameof(be.State),
      BlastFurnaceState.Melting
    );
    ReflectionHelpers.SetField(be, "_moltenIron", 50f);

    ReflectionHelpers.Invoke(be, "Extinguish");

    Assert.Equal(BlastFurnaceState.Idle, be.State);
    Assert.Equal(0f, Iron(be), 3); // the molten pool is gone

    // A solidified-iron block was left in the hearth for the player to mine out.
    var ironBlock = world.World.GetBlock(
      new AssetLocation("smex", "solidifiediron")
    )!;
    var placedAt = (BlockPos)
      ReflectionHelpers.Invoke(be, "GetGlobalPos", 0, -2, 2)!;
    Assert.Equal(ironBlock.BlockId, world.GetBlock(placedAt).BlockId);
  }

  #endregion
}

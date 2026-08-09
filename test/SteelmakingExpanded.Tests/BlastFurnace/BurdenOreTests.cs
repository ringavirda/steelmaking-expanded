using ExpandedLib.Testing;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The burden's ore side: the bell hopper counts crushed ore and raw iron nuggets in one shared ore
/// unit, so a batch can be fed either or any mix of the two. Crushed ore is the denser feed and is
/// spent first, and a feed short of a whole batch makes nothing at all - the same shape as the fuel
/// side in <see cref="BurdenFuelTests"/>.
/// </summary>
public class BurdenOreTests {
  // Feed slot indices on the reinforced hopper above (see InventoryBlastFurnace.NewSlot):
  // 0/1/4/5 iron, 2/6 fuel, 3/7 flux. Crushed ore and nuggets share the iron slots.
  private const int IronSlot = 0;
  private const int SecondIronSlot = 1;
  private const int FuelSlot = 2;
  private const int LimeSlot = 3;

  private const string Crushed = "game:crushed-iron";
  private const string Nugget = "game:nugget-limonite";

  private static readonly BlockPos BellPos = new(0, 16, 0);

  private static BlockEntityHopperBell Bell(TestWorld world) {
    var be = new BlockEntityHopperBell {
      Pos = BellPos,
      Block = TestBlocks.Configure(new Block(), "smex:hopperbell", 90),
    };
    world.Place(BellPos, be.Block, be);
    world.Attach(be);
    return be;
  }

  private static BlockEntityHopperReinforced HopperAbove(TestWorld world) {
    var be = new BlockEntityHopperReinforced {
      // Its own MarkDirty runs whenever a feed slot changes, which needs a position.
      Pos = BellPos.UpCopy(),
      Block = TestBlocks.Configure(new Block(), "smex:hopperreinforced", 91),
    };
    world.Place(BellPos.UpCopy(), be.Block, be);
    world.Attach(be);
    // The block entity constructs its inventory with no Api. DidModifyItemSlot only reaches for
    // Api.World when the slot still holds a stack, so a partly-drawn feed slot NREs where a fully
    // emptied one does not.
    be.Inventory.LateInitialize("hopperreinforced-1", world.Api);
    return be;
  }

  private static void Put(InventoryBase inv, int slot, string code, int count) {
    var item = new Item { Code = new AssetLocation(code) };
    inv[slot].Itemstack = new ItemStack(item, count);
  }

  /// <summary>Everything one burden batch needs except the ore, which each test supplies.</summary>
  private static void PutFuelAndFlux(InventoryBase inv) {
    Put(inv, FuelSlot, "game:coke", SmexValues.HopperCokeRequired);
    Put(inv, LimeSlot, "game:lime", SmexValues.HopperLimeRequired);
  }

  private static int StackSize(InventoryBase inv, int slot) =>
    inv[slot].Empty ? 0 : inv[slot].StackSize;

  private static ItemSlot Source(string code) =>
    new DummySlot(new ItemStack(new Item { Code = new AssetLocation(code) }));

  #region Single-feed batches

  [Fact]
  public void A_batch_fed_crushed_ore_alone_costs_the_configured_crushed_ore() {
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutFuelAndFlux(hopper.Inventory);
    Put(hopper.Inventory, IronSlot, Crushed, SmexValues.HopperIronOreRequired);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, IronSlot));
  }

  [Fact]
  public void A_batch_fed_nuggets_alone_costs_the_configured_nuggets() {
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutFuelAndFlux(hopper.Inventory);
    Put(hopper.Inventory, IronSlot, Nugget, SmexValues.HopperNuggetRequired);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, IronSlot));
  }

  #endregion

  #region Mixed feed

  [Fact]
  public void Half_a_batch_of_each_makes_exactly_one_batch() {
    // At the shipped rates a batch costs 240 ore units: crushed ore is worth 20 and a nugget 12,
    // so half a batch's crushed plus half a batch's nuggets is one whole batch with no remainder.
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutFuelAndFlux(hopper.Inventory);
    Put(
      hopper.Inventory,
      IronSlot,
      Crushed,
      SmexValues.HopperIronOreRequired / 2
    );
    Put(
      hopper.Inventory,
      SecondIronSlot,
      Nugget,
      SmexValues.HopperNuggetRequired / 2
    );

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, IronSlot));
    Assert.Equal(0, StackSize(hopper.Inventory, SecondIronSlot));
  }

  [Fact]
  public void Crushed_ore_is_spent_before_nuggets_when_both_are_loaded() {
    // A batch's worth of each feed is loaded but only one batch of fuel and flux, so the denser
    // feed goes first and the whole nugget stack survives untouched.
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutFuelAndFlux(hopper.Inventory);
    Put(hopper.Inventory, IronSlot, Crushed, SmexValues.HopperIronOreRequired);
    Put(
      hopper.Inventory,
      SecondIronSlot,
      Nugget,
      SmexValues.HopperNuggetRequired
    );

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, IronSlot));
    Assert.Equal(
      SmexValues.HopperNuggetRequired,
      StackSize(hopper.Inventory, SecondIronSlot)
    );
  }

  [Fact]
  public void A_mixed_feed_pays_the_advertised_rate_over_a_run_of_batches() {
    // The last piece of the bulkier feed cannot be split, so unless the two rates divide evenly a
    // mixed batch has to round up. Rounding up every batch quietly charged the player over the
    // advertised rate; the surplus is banked against the next batch instead.
    // The shipped rates are equal, which leaves no remainder to round - so this drives a retuned
    // config where they do not divide, which is the case the banking exists for.
    int nuggetRate = SmexValues.HopperNuggetRequired;
    try {
      SmexValues.Edit(c => c.HopperNuggetRequired = 20);

      var world = new TestWorld();
      var bell = Bell(world);
      var hopper = HopperAbove(world);

      // At 12 crushed / 20 nuggets a batch is 240 units, crushed is worth 20 and a nugget 12.
      // Exactly one crushed ore per batch is the worst split there is: 240 - 20 = 220, and 220 is
      // 18.33 nuggets. Re-stocking between ticks makes every batch hit that split, which is what
      // turns a one-off rounding loss into a growing one.
      const int batches = 3;
      const int nuggetsLoaded = 100;
      Put(hopper.Inventory, SecondIronSlot, Nugget, nuggetsLoaded);

      for (int i = 0; i < batches; i++) {
        Put(hopper.Inventory, IronSlot, Crushed, 1);
        Put(
          hopper.Inventory,
          FuelSlot,
          "game:coke",
          SmexValues.HopperCokeRequired
        );
        Put(
          hopper.Inventory,
          LimeSlot,
          "game:lime",
          SmexValues.HopperLimeRequired
        );
        ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);
      }

      int crushedSpent = batches - StackSize(hopper.Inventory, IronSlot);
      int nuggetsSpent =
        nuggetsLoaded - StackSize(hopper.Inventory, SecondIronSlot);
      int unitsSpent =
        crushedSpent * BurdenValue.OrePerCrushed
        + nuggetsSpent * BurdenValue.OrePerNugget;
      int batchesMade = bell.BurdenMagazine / SmexValues.HopperBurdenProduced;

      Assert.Equal(batches, batchesMade);
      // Never under - that would be free iron - and the total overshoot stays under one nugget for
      // the whole run rather than accruing one per batch.
      Assert.InRange(
        unitsSpent,
        batchesMade * BurdenValue.OrePerBatch,
        batchesMade * BurdenValue.OrePerBatch + BurdenValue.OrePerNugget - 1
      );
    } finally {
      SmexValues.Edit(c => c.HopperNuggetRequired = nuggetRate);
    }
  }

  #endregion

  #region Short feed

  [Theory]
  [InlineData(11, 0)] // one crushed ore short
  [InlineData(0, 11)] // one nugget short
  [InlineData(6, 5)] // half a batch of crushed plus one nugget short of the rest
  [InlineData(0, 0)]
  public void A_feed_short_of_a_whole_batch_of_ore_makes_nothing(
    int crushed,
    int nuggets
  ) {
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutFuelAndFlux(hopper.Inventory);
    if (crushed > 0)
      Put(hopper.Inventory, IronSlot, Crushed, crushed);
    if (nuggets > 0)
      Put(hopper.Inventory, SecondIronSlot, Nugget, nuggets);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(0, bell.BurdenMagazine);
    Assert.Equal(crushed, StackSize(hopper.Inventory, IronSlot));
    Assert.Equal(nuggets, StackSize(hopper.Inventory, SecondIronSlot));
    // The fuel and flux must be left alone too - a short ore feed is not a partial craft.
    Assert.Equal(
      SmexValues.HopperCokeRequired,
      StackSize(hopper.Inventory, FuelSlot)
    );
    Assert.Equal(
      SmexValues.HopperLimeRequired,
      StackSize(hopper.Inventory, LimeSlot)
    );
  }

  #endregion

  #region Iron slot typing

  [Theory]
  [InlineData(Crushed)]
  [InlineData(Nugget)]
  [InlineData("game:nugget-hematite")]
  [InlineData("game:nugget-magnetite")]
  [InlineData("smex:burden")]
  public void The_iron_slot_takes_every_accepted_feed(string code) {
    var inv = new InventoryBlastFurnace(8, "test", null, null);
    var iron = (ItemSlotBlastFurnace)inv[IronSlot];

    Assert.Equal("iron", iron.AllowedType);
    Assert.True(iron.CanTakeFrom(Source(code)));
  }

  [Theory]
  [InlineData("game:nugget-nativecopper")] // a nugget, but not iron-bearing
  [InlineData("game:crushed-galena")]
  [InlineData("game:coke")]
  [InlineData("game:lime")]
  public void The_iron_slot_refuses_everything_else(string code) {
    var inv = new InventoryBlastFurnace(8, "test", null, null);
    var iron = (ItemSlotBlastFurnace)inv[IronSlot];

    Assert.False(iron.CanTakeFrom(Source(code)));
  }

  #endregion

  #region Exchange rate

  [Fact]
  public void A_nugget_is_worth_what_a_piece_of_crushed_ore_is() {
    // The furnace takes its iron however it comes: crushing is a convenience on this route, not a
    // toll. Both feeds also stack to 128, so there is no hauling advantage hiding behind the rate.
    Assert.Equal(BurdenValue.OrePerCrushed, BurdenValue.OrePerNugget);
    Assert.Equal(
      BurdenValue.IronPerOreUnits(BurdenValue.OrePerCrushed),
      BurdenValue.IronPerOreUnits(BurdenValue.OrePerNugget),
      3
    );
  }

  [Fact]
  public void One_batch_of_either_feed_is_worth_one_melt_cycle_of_iron() {
    Assert.Equal(SmexValues.BfIronPerMeltCycle, BurdenValue.IronPerBatch, 3);
    Assert.Equal(
      BurdenValue.IronPerBatch,
      BurdenValue.IronPerOreUnits(BurdenValue.OrePerBatch),
      3
    );
  }

  [Fact]
  public void The_tooltip_yield_matches_what_a_whole_batch_of_that_feed_makes() {
    // What the item tooltips state, derived rather than written down again: at the shipped rates
    // 8.5 units per crushed ore and 5.1 per nugget.
    Assert.Equal(
      BurdenValue.IronPerBatch / SmexValues.HopperIronOreRequired,
      BurdenValue.IronPerOreUnits(BurdenValue.OrePerCrushed),
      3
    );
    Assert.Equal(
      BurdenValue.IronPerBatch / SmexValues.HopperNuggetRequired,
      BurdenValue.IronPerOreUnits(BurdenValue.OrePerNugget),
      3
    );
  }

  #endregion
}

using ExpandedLib.Testing;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The burden's fuel side: the bell hopper counts coke and charcoal in one shared carbon unit, so a
/// burden batch can be fed either fuel or any mix of the two. Coke carries twice the carbon of
/// charcoal, is spent first, and a feed short of a whole batch's carbon makes nothing at all.
/// </summary>
public class BurdenFuelTests {
  // Feed slot indices on the reinforced hopper above (see InventoryBlastFurnace.NewSlot):
  // 0/1/4/5 iron, 2/6 fuel, 3/7 flux. Coke and charcoal share the fuel slots.
  private const int IronSlot = 0;
  private const int FuelSlot = 2;
  private const int SecondFuelSlot = 6;
  private const int LimeSlot = 3;

  private const string Coke = "game:coke";
  private const string Charcoal = "game:charcoal";

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
      Block = TestBlocks.Configure(new Block(), "smex:hopperreinforced", 91),
    };
    world.Place(BellPos.UpCopy(), be.Block, be);
    world.Attach(be);
    return be;
  }

  private static void Put(InventoryBase inv, int slot, string code, int count) {
    var item = new Item { Code = new AssetLocation(code) };
    inv[slot].Itemstack = new ItemStack(item, count);
  }

  /// <summary>Everything one burden batch needs except the fuel, which each test supplies.</summary>
  private static void PutOreAndFlux(InventoryBase inv) {
    Put(inv, IronSlot, "game:crushed-iron", SmexValues.HopperIronOreRequired);
    Put(inv, LimeSlot, "game:lime", SmexValues.HopperLimeRequired);
  }

  private static int StackSize(InventoryBase inv, int slot) =>
    inv[slot].Empty ? 0 : inv[slot].StackSize;

  private static ItemSlot Source(string code) =>
    new DummySlot(new ItemStack(new Item { Code = new AssetLocation(code) }));

  #region Single-fuel batches

  [Fact]
  public void A_batch_fed_coke_alone_costs_the_configured_coke() {
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutOreAndFlux(hopper.Inventory);
    Put(hopper.Inventory, FuelSlot, Coke, SmexValues.HopperCokeRequired);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, FuelSlot));
  }

  [Fact]
  public void A_batch_fed_charcoal_alone_costs_the_configured_charcoal() {
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutOreAndFlux(hopper.Inventory);
    Put(
      hopper.Inventory,
      FuelSlot,
      Charcoal,
      SmexValues.HopperCharcoalRequired
    );

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, FuelSlot));
  }

  #endregion

  #region Mixed feed

  [Fact]
  public void One_coke_and_two_charcoal_make_exactly_one_batch() {
    // At the shipped rates a batch costs 8 carbon: coke is worth 4 and charcoal 2, so half a
    // batch's coke plus half a batch's charcoal is one whole batch and nothing is left over.
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutOreAndFlux(hopper.Inventory);
    Put(hopper.Inventory, FuelSlot, Coke, 1);
    Put(hopper.Inventory, SecondFuelSlot, Charcoal, 2);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, FuelSlot));
    Assert.Equal(0, StackSize(hopper.Inventory, SecondFuelSlot));
  }

  [Fact]
  public void Coke_is_spent_before_charcoal_when_both_are_loaded() {
    // A batch's worth of each fuel is loaded but only one batch of ore, so the denser fuel goes
    // first and the whole charcoal stack survives untouched.
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutOreAndFlux(hopper.Inventory);
    Put(hopper.Inventory, FuelSlot, Coke, SmexValues.HopperCokeRequired);
    Put(
      hopper.Inventory,
      SecondFuelSlot,
      Charcoal,
      SmexValues.HopperCharcoalRequired
    );

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(SmexValues.HopperBurdenProduced, bell.BurdenMagazine);
    Assert.Equal(0, StackSize(hopper.Inventory, FuelSlot));
    Assert.Equal(
      SmexValues.HopperCharcoalRequired,
      StackSize(hopper.Inventory, SecondFuelSlot)
    );
  }

  #endregion

  #region Short feed

  [Theory]
  [InlineData(0, 3)] // three charcoal is 6 of the 8 carbon a batch costs
  [InlineData(1, 0)] // one coke is 4
  [InlineData(1, 1)] // 4 + 2
  [InlineData(0, 0)]
  public void A_feed_short_of_a_whole_batch_of_carbon_makes_nothing(
    int coke,
    int charcoal
  ) {
    var world = new TestWorld();
    var bell = Bell(world);
    var hopper = HopperAbove(world);

    PutOreAndFlux(hopper.Inventory);
    if (coke > 0)
      Put(hopper.Inventory, FuelSlot, Coke, coke);
    if (charcoal > 0)
      Put(hopper.Inventory, SecondFuelSlot, Charcoal, charcoal);

    ReflectionHelpers.Invoke(bell, "OnServerTick", 1f);

    Assert.Equal(0, bell.BurdenMagazine);
    Assert.Equal(coke, StackSize(hopper.Inventory, FuelSlot));
    Assert.Equal(charcoal, StackSize(hopper.Inventory, SecondFuelSlot));
    // The ore and flux must be left alone too - a short fuel feed is not a partial craft.
    Assert.Equal(
      SmexValues.HopperIronOreRequired,
      StackSize(hopper.Inventory, IronSlot)
    );
    Assert.Equal(
      SmexValues.HopperLimeRequired,
      StackSize(hopper.Inventory, LimeSlot)
    );
  }

  #endregion

  #region Fuel slot typing

  [Fact]
  public void The_fuel_slot_takes_coke_and_charcoal_and_nothing_else() {
    var inv = new InventoryBlastFurnace(8, "test", null, null);
    var fuel = (ItemSlotBlastFurnace)inv[FuelSlot];

    Assert.Equal("coke", fuel.AllowedType);
    Assert.True(fuel.CanTakeFrom(Source(Coke)));
    Assert.True(fuel.CanTakeFrom(Source(Charcoal)));
    Assert.False(fuel.CanTakeFrom(Source("game:lime")));
    Assert.False(fuel.CanTakeFrom(Source("game:crushed-iron")));
  }

  #endregion
}

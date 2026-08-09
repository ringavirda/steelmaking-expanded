using ExpandedLib.Testing;
using NSubstitute;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using Vintagestory.API.Common;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The boiler's manual bucket fill/drain entry guards. Only a liquid container holding water (or,
/// for draining, an empty one) may interact, so an unrelated held item is a no-op and never touches
/// the water level. The transfer happy-path itself runs through vanilla
/// <c>BlockLiquidContainerBase</c> litre metering, which needs the game's water-tight-container
/// props - out of reach for the headless harness - so these pin the reject branches that protect the
/// water bookkeeping.
/// </summary>
public class BoilerManualWaterTests {
  private static BlockEntityBoilerCornish Boiler(float water) {
    var be = new BlockEntityBoilerCornish();
    ReflectionHelpers.SetField(be, "_waterVolume", water);
    return be;
  }

  private static ItemSlot HeldItem(string code) =>
    new DummySlot(new ItemStack(new Item { Code = new AssetLocation(code) }));

  private static float WaterOf(BlockEntityBoilerCornish be) =>
    (float)ReflectionHelpers.GetField(be, "_waterVolume")!;

  [Fact]
  public void Filling_rejects_a_held_item_that_is_not_a_liquid_container() {
    var be = Boiler(water: 100f);
    Assert.False(
      be.TryManualFill(Substitute.For<IPlayer>(), HeldItem("game:rock-granite"))
    );
    Assert.Equal(100f, WaterOf(be), 3);
  }

  [Fact]
  public void Filling_rejects_an_empty_hand() {
    var be = Boiler(water: 100f);
    Assert.False(be.TryManualFill(Substitute.For<IPlayer>(), new DummySlot()));
    Assert.Equal(100f, WaterOf(be), 3);
  }

  [Fact]
  public void Draining_rejects_a_held_item_that_is_not_a_liquid_container() {
    var be = Boiler(water: 400f);
    Assert.False(
      be.TryManualDrain(
        Substitute.For<IPlayer>(),
        HeldItem("game:rock-granite")
      )
    );
    Assert.Equal(400f, WaterOf(be), 3);
  }
}

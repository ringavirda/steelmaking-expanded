using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using Vintagestory.API.Common;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The shared metal-bit recovery builder behind every chisel/break drop (canal cells, the molten
/// barrel, the bessemer charge). Pins the unit-per-bit ratio, the temperature carry, and the slag
/// fallback so the holders stay in lockstep through <see cref="MoltenChisel.BuildRecovery"/>.
/// </summary>
public class MoltenChiselTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem("game:metalbit-iron");
    world.RegisterItem("smex:slag");
    return world;
  }

  [Theory]
  [InlineData(20, 5, 4)] // canal/converter ratio
  [InlineData(20, 10, 2)] // barrel chisel ratio
  [InlineData(3, 5, 1)] // always at least one bit
  public void BuildRecovery_scales_bits_by_the_units_per_bit(
    int units,
    int unitsPerBit,
    int expectedBits
  ) {
    var world = NewWorld();

    ItemStack? drop = MoltenChisel.BuildRecovery(
      world.World,
      new AssetLocation(Iron),
      900f,
      units,
      unitsPerBit
    );

    Assert.NotNull(drop);
    Assert.Equal("game:metalbit-iron", drop!.Collectible.Code.ToString());
    Assert.Equal(expectedBits, drop.StackSize);
  }

  [Fact]
  public void BuildRecovery_carries_the_metal_temperature() {
    var world = NewWorld();

    ItemStack drop = MoltenChisel.BuildRecovery(
      world.World,
      new AssetLocation(Iron),
      850f,
      20
    )!;

    Assert.Equal(850f, drop.Collectible.GetTemperature(world.World, drop), 0);
  }

  [Fact]
  public void BuildRecovery_returns_null_when_the_solid_item_is_missing_and_no_slag_fallback() {
    var world = NewWorld(); // no metalbit-gold registered

    Assert.Null(
      MoltenChisel.BuildRecovery(
        world.World,
        new AssetLocation("game:ingot-gold"),
        900f,
        20
      )
    );
  }

  [Fact]
  public void BuildRecovery_falls_back_to_slag_when_requested() {
    var world = NewWorld();

    ItemStack? drop = MoltenChisel.BuildRecovery(
      world.World,
      new AssetLocation("game:ingot-gold"),
      900f,
      20,
      slagFallback: true
    );

    Assert.NotNull(drop);
    Assert.Equal("smex:slag", drop!.Collectible.Code.ToString());
    Assert.Equal(4, drop.StackSize);
  }
}

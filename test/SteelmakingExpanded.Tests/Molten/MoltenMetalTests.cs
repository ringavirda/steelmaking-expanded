using SteelmakingExpanded.BlockNetworkMolten;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The shared molten-metal value helper: the incandescent glow scale and the metal-name formatting
/// that every canal cell, tap, pedestal and barrel reads. Temperature/melt-point classification needs
/// a resolved collectible, so only the world-free arithmetic is pinned here.
/// </summary>
public class MoltenMetalTests {
  [Theory]
  [InlineData(20f, 0)] // cold
  [InlineData(499f, 0)] // just below the glow floor
  [InlineData(500f, 0)] // floor is exclusive (> GlowMinTemp)
  [InlineData(530f, 1)] // (530-500)/30 = 1
  [InlineData(800f, 10)] // (800-500)/30 = 10
  [InlineData(5000f, 24)] // clamped to the 24 ceiling
  public void GlowLevel_scales_from_the_glow_floor_and_clamps(
    float temp,
    int expected
  ) {
    Assert.Equal((byte)expected, MoltenMetal.GlowLevel(temp));
  }

  [Theory]
  [InlineData("game:ingot-iron", "Iron")] // ingot- prefix dropped, capitalised
  [InlineData("game:ingot-steel", "Steel")]
  [InlineData("smex:slag", "Slag")] // non-ingot path used verbatim
  [InlineData("game:metalbit-copper", "Metalbit-copper")]
  public void DisplayName_strips_ingot_prefix_and_capitalises(
    string code,
    string expected
  ) {
    Assert.Equal(expected, MoltenMetal.DisplayName(code));
  }

  [Fact]
  public void Thresholds_are_ordered_hardened_below_liquid() {
    Assert.True(MoltenMetal.HardenedThreshold < MoltenMetal.LiquidThreshold);
  }

  [Fact]
  public void FormatTemperature_reads_cold_below_room_temperature() {
    // Below 21 C it prints the "cold" label (here the echoed lang key), not a number.
    Assert.Equal("smex:metalstate-cold", MoltenMetal.FormatTemperature(15f));
  }

  [Fact]
  public void FormatTemperature_prints_the_rounded_value_when_warm() {
    // ExMeasure.Temperature in the default metric system prints "650 <unit>".
    Assert.StartsWith("650 ", MoltenMetal.FormatTemperature(650f));
  }
}

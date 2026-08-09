using System;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockStructures.Boiler;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The boiler's pressure/temperature formulas, exercised on a real (Cornish) boiler entity with its
/// water/steam state primed directly. These are the numbers that drive the danger zone and the steam
/// the engines see, so the formulas are pinned independently of the heavily game-coupled tick loop.
/// </summary>
public class BoilerMathTests {
  // The Cornish boiler's stats come from config POCO defaults (no Load needed headlessly).
  private const float Capacity = 800f; // CornishBoilerCapacity
  private const float ChokePressure = 5f; // CornishBoilerMaxOutputPressure

  private static BlockEntityBoilerCornish Boiler(
    float water,
    float steam,
    BlockEntityBoiler.BoilerState state = BlockEntityBoiler.BoilerState.Idle,
    float heatingSeconds = 0f
  ) {
    var be = new BlockEntityBoilerCornish();
    ReflectionHelpers.SetField(be, "_waterVolume", water);
    ReflectionHelpers.SetField(be, "_steamVolume", steam);
    ReflectionHelpers.SetField(be, "_state", state);
    ReflectionHelpers.SetField(be, "_heatingSeconds", heatingSeconds);
    return be;
  }

  [Fact]
  public void InternalPressure_is_steam_over_free_vessel_space() {
    var be = Boiler(water: 300f, steam: 200f);
    // 200 / (800 - 300) = 0.4
    Assert.Equal(0.4f, be.InternalPressure, 3);
  }

  [Fact]
  public void InternalPressure_clamps_the_denominator_to_avoid_blow_up() {
    // Water at full capacity would make the free space zero; the formula floors it at 1 L.
    var be = Boiler(water: Capacity, steam: 50f);
    Assert.Equal(50f, be.InternalPressure, 3);
  }

  [Fact]
  public void SteamTemperature_reads_the_boiling_point_at_zero_gauge_pressure() {
    var be = Boiler(water: 0f, steam: 0f); // 0 atm gauge
    float temp = (float)ReflectionHelpers.Invoke(be, "SteamTemperature")!;
    Assert.Equal(PpexValues.BoilingPoint, temp, 3);
  }

  [Fact]
  public void SteamTemperature_follows_the_saturation_curve() {
    var be = Boiler(water: 0f, steam: 1600f); // P = 1600/800 = 2 atm gauge
    float expected =
      PpexValues.BoilingPoint
      * (float)Math.Pow(2f + 1f, PpexValues.SteamSaturationExponent);
    float temp = (float)ReflectionHelpers.Invoke(be, "SteamTemperature")!;
    Assert.Equal(expected, temp, 3);
  }

  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(90f, 0.5f)] // half of the 180 s heat-up
  [InlineData(180f, 1f)]
  [InlineData(500f, 1f)] // clamped at 1
  public void HeatProgress_tracks_the_heat_up_clock(
    float heatingSeconds,
    float expected
  ) {
    var be = Boiler(0f, 0f, heatingSeconds: heatingSeconds);
    Assert.Equal(expected, be.HeatProgress, 3);
  }

  [Fact]
  public void DangerZone_trips_only_while_boiling_near_the_choke_pressure() {
    // 0.9 * 5 atm = 4.5 atm threshold; free space 800 L -> need 3680 L steam for 4.6 atm.
    var hot = Boiler(
      water: 0f,
      steam: 4.6f * Capacity,
      state: BlockEntityBoiler.BoilerState.Boiling
    );
    Assert.True(hot.InternalPressure >= 0.9f * ChokePressure);
    Assert.True(hot.InDangerZone);
  }

  [Fact]
  public void DangerZone_is_clear_below_the_threshold() {
    var warm = Boiler(
      water: 0f,
      steam: 4.0f * Capacity, // 4 atm, below the 4.5 threshold
      state: BlockEntityBoiler.BoilerState.Boiling
    );
    Assert.False(warm.InDangerZone);
  }

  [Fact]
  public void DangerZone_is_clear_when_not_boiling_even_at_high_pressure() {
    var idleButHot = Boiler(
      water: 0f,
      steam: 4.6f * Capacity,
      state: BlockEntityBoiler.BoilerState.Idle
    );
    Assert.False(idleButHot.InDangerZone);
  }

  [Fact]
  public void A_fresh_boiler_is_not_constructed_or_operational() {
    var be = Boiler(0f, 0f);
    Assert.False(be.IsConstructed);
    Assert.False(be.IsOperational);
  }
}

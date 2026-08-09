using PipesAndPowerExpanded.Helpers;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The display-unit helper: the simulation runs in metric, and these methods convert a metric value
/// into the player's chosen system. The numeric conversion (litres->gallons, atm->psi, C->F) is
/// pinned here; the trailing unit symbol is localized, so assertions check the number, not the label.
/// </summary>
[Collection("ExMeasure")] // mutates the global ExMeasure.System; must not run in parallel
public class ExMeasureTests : System.IDisposable {
  private readonly MeasurementSystem _original = ExMeasure.System;

  public void Dispose() => ExMeasure.System = _original;

  [Theory]
  [InlineData("imperial", MeasurementSystem.Imperial)]
  [InlineData("Imperial", MeasurementSystem.Imperial)]
  [InlineData("IMPERIAL", MeasurementSystem.Imperial)]
  [InlineData("metric", MeasurementSystem.Metric)]
  [InlineData("nonsense", MeasurementSystem.Metric)]
  [InlineData(null, MeasurementSystem.Metric)]
  public void Parse_reads_system_names_case_insensitively(
    string? name,
    MeasurementSystem expected
  ) {
    Assert.Equal(expected, ExMeasure.Parse(name));
  }

  [Fact]
  public void Metric_volume_prints_the_value_unchanged() {
    ExMeasure.System = MeasurementSystem.Metric;
    Assert.StartsWith("800 ", ExMeasure.Volume(800f));
  }

  [Fact]
  public void Imperial_volume_converts_litres_to_gallons() {
    ExMeasure.System = MeasurementSystem.Imperial;
    // 100 L * 0.21997 = 21.997 -> "22" at F0
    Assert.StartsWith("22 ", ExMeasure.Volume(100f));
  }

  [Fact]
  public void Imperial_pressure_converts_atm_to_psi() {
    ExMeasure.System = MeasurementSystem.Imperial;
    // 5 atm * 14.696 = 73.48 -> "73.48" at F2
    Assert.StartsWith("73.48 ", ExMeasure.Pressure(5f));
  }

  [Fact]
  public void Imperial_temperature_converts_celsius_to_fahrenheit() {
    ExMeasure.System = MeasurementSystem.Imperial;
    // 100 C -> 212 F
    Assert.StartsWith("212 ", ExMeasure.Temperature(100f));
  }

  /// <summary>
  /// A difference in temperature scales but does not shift: the freezing-point offset belongs to a
  /// point on the scale, not to a distance along it. The converter's scrap penalty is a delta and
  /// read 32 °F too high through the absolute formatter.
  /// </summary>
  [Fact]
  public void Imperial_temperature_delta_scales_without_the_freezing_offset() {
    ExMeasure.System = MeasurementSystem.Imperial;
    // A 16 C drop is a 28.8 F drop -> "29" at F0, not 60.8.
    Assert.StartsWith("29 ", ExMeasure.TemperatureDelta(16f));
    Assert.StartsWith("576 ", ExMeasure.TemperatureDelta(320f));
  }

  [Fact]
  public void Metric_temperature_delta_is_the_figure_itself() {
    ExMeasure.System = MeasurementSystem.Metric;
    Assert.StartsWith("16 ", ExMeasure.TemperatureDelta(16f));
  }

  [Fact]
  public void Volume_range_prints_the_unit_once_after_the_pair() {
    ExMeasure.System = MeasurementSystem.Metric;
    var s = ExMeasure.VolumeRange(400f, 800f);
    Assert.StartsWith("400 / 800 ", s);
  }

  [Fact]
  public void ConvertMetricText_returns_input_unchanged_in_metric_mode() {
    ExMeasure.System = MeasurementSystem.Metric;
    const string text = "Holds 30 L at 2-4 atm.";
    Assert.Equal(text, ExMeasure.ConvertMetricText(text));
  }

  [Fact]
  public void ConvertMetricText_passes_through_text_without_units() {
    ExMeasure.System = MeasurementSystem.Imperial;
    Assert.Equal("no units here", ExMeasure.ConvertMetricText("no units here"));
  }
}

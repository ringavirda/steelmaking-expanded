using System;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Vintagestory.API.Config;

namespace PipesAndPowerExpanded.Helpers;

/// <summary>
/// The unit system used when formatting measurements for the look-at HUD / block info / handbook.
/// The simulation itself always runs in metric; this only changes how values are displayed.
/// </summary>
public enum MeasurementSystem {
  /// <summary>Litres, atmospheres, degrees Celsius.</summary>
  Metric,

  /// <summary>Imperial gallons, pounds per square inch, degrees Fahrenheit.</summary>
  Imperial,
}

/// <summary>
/// Shared display-unit helper for the steam/pipe machinery (used by ppex and, through its dependency
/// on ppex, smex). All gameplay values are stored and calculated in metric (litres, atm, °C); these
/// methods convert a metric value into a player-facing string in the currently active
/// <see cref="System"/>, so block-info / handbook code can stay unit-agnostic.
/// <para>
/// <see cref="System"/> is a per-player, client-side preference: it is driven by
/// <see cref="Preferences.MeasurePreference"/> and persisted through the library's generic
/// preferences store, applied for the local player on join and changed via <c>.exmod measure</c>.
/// </para>
/// </summary>
public static class ExMeasure {
  /// <summary>The display system currently active on this client (the local player's choice).
  /// Block-info formatters read this; it is set from the per-player preference on join.</summary>
  public static MeasurementSystem System { get; set; } =
    MeasurementSystem.Metric;

  // Conversion factors from metric.
  private const float LitresToImperialGallons = 0.219969248f; // 1 L = 0.21997 imp gal
  private const float AtmToPsi = 14.6959488f; // 1 atm = 14.696 psi

  private static bool Imperial => System == MeasurementSystem.Imperial;

  /// <summary>Parses a system name ("metric" / "imperial", case-insensitive); returns
  /// <see cref="MeasurementSystem.Metric"/> for anything unrecognised.</summary>
  public static MeasurementSystem Parse(string? name) =>
    string.Equals(name, "imperial", StringComparison.OrdinalIgnoreCase)
      ? MeasurementSystem.Imperial
      : MeasurementSystem.Metric;

  #region Volume

  /// <summary>A volume given in litres, e.g. <c>"800 L"</c> or <c>"176 gal"</c>.</summary>
  public static string Volume(float litres, string format = "F0") {
    var (num, unit) = Vol(litres, format);
    return num + " " + unit;
  }

  /// <summary>A volume range from a shared pool, e.g. <c>"400 / 800 L"</c> - the unit is
  /// printed once after the pair.</summary>
  public static string VolumeRange(
    float litres,
    float maxLitres,
    string format = "F0"
  ) {
    var (a, _) = Vol(litres, format);
    var (b, unit) = Vol(maxLitres, format);
    return a + " / " + b + " " + unit;
  }

  /// <summary>A flow rate given in litres per second, e.g. <c>"48 L/s"</c> or <c>"11 gal/s"</c>.</summary>
  public static string FlowRate(float litresPerSecond, string format = "F1") {
    var (num, unit) = Flow(litresPerSecond, format);
    return num + " " + unit;
  }

  #endregion

  #region Pressure

  /// <summary>A pressure given in atmospheres, e.g. <c>"5.00 atm"</c> or <c>"73.48 psi"</c>.</summary>
  public static string Pressure(float atm, string format = "F2") {
    var (num, unit) = Pres(atm, format);
    return num + " " + unit;
  }

  /// <summary>A pressure range (current / limit), e.g. <c>"3.0 / 8.0 atm"</c> - the unit is
  /// printed once after the pair.</summary>
  public static string PressureRange(
    float atm,
    float maxAtm,
    string format = "F1"
  ) {
    var (a, _) = Pres(atm, format);
    var (b, unit) = Pres(maxAtm, format);
    return a + " / " + b + " " + unit;
  }

  #endregion

  #region Temperature

  /// <summary>A temperature given in degrees Celsius, e.g. <c>"100 °C"</c> or <c>"212 °F"</c>.</summary>
  public static string Temperature(float celsius, string format = "F0") {
    var (num, unit) = Temp(celsius, format);
    return num + " " + unit;
  }

  /// <summary>
  /// A temperature DIFFERENCE in degrees Celsius - how much hotter or colder, not how hot. Scaled
  /// into Fahrenheit without the freezing-point offset, which belongs to a point on the scale and
  /// not to a distance along it: a 16 °C penalty is 29 °F, never 61.
  /// </summary>
  public static string TemperatureDelta(
    float celsiusDelta,
    string format = "F0"
  ) =>
    Num(Imperial ? celsiusDelta * 9f / 5f : celsiusDelta, format)
    + " "
    + Unit(Imperial ? "fahrenheit" : "celsius");

  #endregion

  #region Handbook prose conversion

  /// <summary>One metric→imperial unit conversion, keyed by the <em>localized</em> metric symbol
  /// so handbook prose authored in any locale's units converts: how to turn a metric value
  /// imperial, the number format to print it with, and the localized imperial symbol.</summary>
  private readonly record struct UnitConversion(
    string MetricSymbol,
    string ImperialSymbol,
    Func<float, float> ToImperial,
    string Format
  );

  // The metric-literal regex and conversion table are derived from the localized unit symbols, so
  // they're rebuilt whenever the active language changes those symbols (keyed by _conversionSig).
  private static string? _conversionSig;
  private static Regex? _metricRegex;
  private static UnitConversion[]? _conversions;

  private static void EnsureConversions() {
    var conversions = new[]
    {
      new UnitConversion(
        Unit("litres-per-second"),
        Unit("gallons-per-second"),
        v => v * LitresToImperialGallons,
        "0.##"
      ),
      new UnitConversion(
        Unit("litres"),
        Unit("gallons"),
        v => v * LitresToImperialGallons,
        "0.##"
      ),
      new UnitConversion(Unit("atm"), Unit("psi"), v => v * AtmToPsi, "0.##"),
      new UnitConversion(
        Unit("celsius"),
        Unit("fahrenheit"),
        v => v * 9f / 5f + 32f,
        "0"
      ),
    };

    // U+0001 joins the symbols because no unit symbol can contain it, so the signature cannot
    // collide with one built from a different split. Written as an escape: a literal control byte
    // is invisible in an editor, which is how nine of them went unnoticed in a recipe file.
    string sig = string.Join("\u0001", conversions.Select(c => c.MetricSymbol));
    if (sig == _conversionSig && _metricRegex != null)
      return;

    // Longest metric symbol first so a compound symbol wins over its prefix (e.g. "L/s" over "L");
    // each symbol is regex-escaped since a locale may use characters with regex meaning. The whole
    // number expression - a value, an "a-b" range or an "a / b / c" list - is captured, and the
    // trailing negative look-ahead stops a symbol from matching inside a Latin word.
    string alternation = string.Join(
      "|",
      conversions
        .OrderByDescending(c => c.MetricSymbol.Length)
        .Select(c => Regex.Escape(c.MetricSymbol))
    );
    _metricRegex = new Regex(
      @"((?:\d+(?:\.\d+)?\s*[-/]\s*)*\d+(?:\.\d+)?)\s*("
        + alternation
        + @")(?![A-Za-z])",
      RegexOptions.Compiled
    );
    _conversions = conversions;
    _conversionSig = sig;
  }

  /// <summary>
  /// Converts plain metric unit mentions in free prose (e.g. handbook text) to the active
  /// <see cref="System"/>: "30 L" → "6.6 gal", "2-4 atm" → "29.39-58.78 psi", "160-220 °C" →
  /// "320-428 °F", "8 / 16 / 32 L/s" → "1.76 / 3.52 / 7.04 gal/s". The metric symbols matched and
  /// the imperial symbols printed are the localized <c>unit-*</c> labels, so a translated handbook
  /// converts too (author the prose with the same metric symbols the lang file defines). Returns
  /// the text unchanged in metric mode and for any text without a recognised unit.
  /// </summary>
  public static string ConvertMetricText(string text) {
    if (!Imperial || string.IsNullOrEmpty(text))
      return text;

    EnsureConversions();

    return _metricRegex!.Replace(
      text,
      m => {
        string numbers = m.Groups[1].Value;
        string symbol = m.Groups[2].Value;
        UnitConversion conv = Array.Find(
          _conversions!,
          c => c.MetricSymbol == symbol
        );
        // Convert each number in the (possibly multi-value) expression, keeping the
        // original "-" / "/" separators and spacing between them.
        string converted = Regex.Replace(
          numbers,
          @"\d+(?:\.\d+)?",
          nm =>
            Num(
              conv.ToImperial(
                float.Parse(nm.Value, CultureInfo.InvariantCulture)
              ),
              conv.Format
            )
        );
        return converted + " " + conv.ImperialSymbol;
      }
    );
  }

  #endregion

  #region Conversions

  private static (string num, string unit) Vol(float litres, string format) =>
    Imperial
      ? (Num(litres * LitresToImperialGallons, format), Unit("gallons"))
      : (Num(litres, format), Unit("litres"));

  private static (string num, string unit) Flow(
    float litresPerSecond,
    string format
  ) =>
    Imperial
      ? (
        Num(litresPerSecond * LitresToImperialGallons, format),
        Unit("gallons-per-second")
      )
      : (Num(litresPerSecond, format), Unit("litres-per-second"));

  private static (string num, string unit) Pres(float atm, string format) =>
    Imperial
      ? (Num(atm * AtmToPsi, format), Unit("psi"))
      : (Num(atm, format), Unit("atm"));

  private static (string num, string unit) Temp(float celsius, string format) =>
    Imperial
      ? (Num(celsius * 9f / 5f + 32f, format), Unit("fahrenheit"))
      : (Num(celsius, format), Unit("celsius"));

  /// <summary>Localized symbol for a unit, e.g. <c>Unit("litres")</c> → "L" / "л".</summary>
  private static string Unit(string key) => Lang.Get("ppex:unit-" + key);

  // Formats with the invariant culture so the decimal separator stays a dot regardless of
  // the player's locale (matching how the rest of the HUD numbers read).
  private static string Num(float value, string format) =>
    value.ToString(format, CultureInfo.InvariantCulture);

  #endregion
}

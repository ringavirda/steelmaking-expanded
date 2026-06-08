using System;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded;

/// <summary>
/// JSON-serializable gameplay tunables for Pipes and Power Expanded. Loaded from (and written
/// to) <c>ModConfig/ppex.json</c>; the property defaults below apply when the file is missing or
/// a key is absent. Accessed through <see cref="PpexValues"/>, not directly.
/// </summary>
public class PpexConfig
{
  #region Pipes
  /// <summary>Per-material pipe burst pressure (atm) — the weakest pipe limits a run.</summary>
  public float CopperPipeBurstPressure { get; set; } = 1.5f;
  public float BronzePipeBurstPressure { get; set; } = 3.0f;
  public float BrassPipeBurstPressure { get; set; } = 3.0f;
  public float IronPipeBurstPressure { get; set; } = 5.0f;
  public float SteelPipeBurstPressure { get; set; } = 10.0f;

  /// <summary>Glow factor applied to a ferric (iron/steel) pipe's temperature.</summary>
  public float FerricGlowFactor { get; set; } = 0.6f;

  /// <summary>Temperature (°C) at which water boils into steam / steam condenses into water.</summary>
  public float BoilingPoint { get; set; } = 100f;

  /// <summary>Temperature (°C) lost per ferric (iron/steel) pipe as gas/fluid travels away from the source.</summary>
  public float FerricPipeHeatLoss { get; set; } = 0.5f;

  /// <summary>Temperature (°C) lost per non-ferric (copper/bronze/brass) pipe as gas/fluid travels.</summary>
  public float NonFerricPipeHeatLoss { get; set; } = 2f;

  /// <summary>Gas (m³/s) a vanilla chimney atop a pipe vents from the network.</summary>
  public float ChimneyVentRate { get; set; } = 2.0f;

  /// <summary>Liquid (m³/s) lost per open-ended pipe connector (leak).</summary>
  public float LiquidLeakRatePerOpening { get; set; } = 0.0005f;
  #endregion

  #region Boiler (Base values used for Lancashire variant)
  /// <summary>Max output-network pressure (atm) a boiler can vent exhaust into; above it the fire goes out.</summary>
  public float ExhaustMaxOutputPressure { get; set; } = 0.8f;

  /// <summary>Steam pressure (atm) the boiler chokes at — it stops producing steam above this on the outlet network.</summary>
  public float BoilerMaxOutputPressure { get; set; } = 12.0f;

  /// <summary>Seconds a boiler may sit fully choked (steam buffer full, still firing and boiling
  /// water with nowhere to vent) before it explodes. Open the lid or give the steam somewhere to go.</summary>
  public float BoilerOverpressureSeconds { get; set; } = 30f;

  /// <summary>How much faster coal burns inside a boiler than a free-standing pile.</summary>
  public float BoilerCoalBurnRateMultiplier { get; set; } = 4.0f;

  /// <summary>Cap (°C) on the Lancashire boiler's working steam temperature (~190 °C saturated at 12 atm).</summary>
  public float BoilerMaxTemperature { get; set; } = 200f;

  /// <summary>Base heating-rate factor for the boiler firebox (divided by water amount for thermal mass).</summary>
  public float BoilerHeatingSpeed { get; set; } = 0.05f;

  /// <summary>Reference water volume (m³) for the thermal-mass divisor (rate is scaled by water / this).</summary>
  public float BoilerThermalMassReference { get; set; } = 0.1f;

  /// <summary>Maximum water (m³) the boiler can hold.</summary>
  public float BoilerWaterCapacity { get; set; } = 0.4f;

  /// <summary>Maximum internal steam (m³) the boiler can hold before venting/choking caps it.</summary>
  public float BoilerMaxInternalSteam { get; set; } = 20f;

  /// <summary>Steam (m³) produced per second while boiling at full tilt (supplies ~2 Cornish engines).</summary>
  public float BoilerSteamPerSecond { get; set; } = 4.5f;

  /// <summary>Water (m³) consumed per m³ of steam produced — a ~200× expansion (and the reverse
  /// ratio steam condenses back at). Also the litres-equivalent the engine/condenser recover.</summary>
  public float BoilerWaterPerSteam { get; set; } = 0.005f;

  /// <summary>Block radius damaged when a boiler explodes.</summary>
  public int BoilerExplosionRadius { get; set; } = 4;

  /// <summary>How fast (per second) an open lid bleeds internal pressure/temperature toward the vent targets.</summary>
  public float BoilerLidVentSpeed { get; set; } = 0.5f;

  /// <summary>Temperature (°C) an open lid rapidly cools the boiler toward.</summary>
  public float BoilerLidCoolTarget { get; set; } = 60f;

  /// <summary>Internal pressure (atm) an open lid rapidly bleeds the boiler toward.</summary>
  public float BoilerLidVentPressure { get; set; } = 1.0f;
  #endregion

  #region Cornish boiler
  /// <summary>Maximum water (m³) the Cornish boiler can hold (≈2/3 of the Lancashire vessel).</summary>
  public float CornishBoilerWaterCapacity { get; set; } = 0.27f;

  /// <summary>Maximum internal steam (m³) the Cornish boiler can hold (≈2/3 of the Lancashire).</summary>
  public float CornishBoilerMaxInternalSteam { get; set; } = 13f;

  /// <summary>Steam (m³/s) the Cornish boiler produces while boiling at full tilt (supplies ~1 Watt engine).</summary>
  public float CornishBoilerSteamPerSecond { get; set; } = 3.5f;

  /// <summary>Steam pressure (atm) the Cornish boiler chokes at — matches the Watt engine's band top so a Watt
  /// engine it feeds never sits over-pressure, and stays well below the Cornish engine's 6 atm floor.</summary>
  public float CornishBoilerMaxOutputPressure { get; set; } = 4.0f;

  /// <summary>Cap (°C) on the Cornish boiler's working steam temperature (~150 °C saturated at 5 atm).</summary>
  public float CornishBoilerMaxTemperature { get; set; } = 160f;
  #endregion

  #region Engines + sub-machines
  /// <summary>Bottom of the Watt engine's operating band (atm) — below this it idles.</summary>
  public float WattEngineMinPressure { get; set; } = 2.0f;

  /// <summary>Top of the Watt engine's normal band (atm) — full power at this pressure; sustained operation
  /// above it for <see cref="EngineOverPressureSeconds"/> breaks the engine.</summary>
  public float WattEngineMaxPressure { get; set; } = 4.0f;

  /// <summary>Maximum power a Watt engine delivers (at the top of its band).</summary>
  public float WattEngineMaxPower { get; set; } = 0.8f;

  /// <summary>Steam (m³/s) a Watt engine consumes per unit power (thirsty — fills the whole cylinder).</summary>
  public float WattEngineSteamPerPower { get; set; } = 4f;

  /// <summary>Inlet pressure (atm) a fully underclocked Cornish engine engages at (lowest control-rod setting).</summary>
  public float CornishEngineUnderclockPressure { get; set; } = 4.0f;

  /// <summary>Inlet pressure (atm) a fully overclocked Cornish engine engages at (highest control-rod setting).</summary>
  public float CornishEngineOverclockPressure { get; set; } = 8.0f;

  /// <summary>How far above its engage pressure (atm) a Cornish engine tolerates before the over-pressure timer
  /// starts — so its break threshold rides up with the throttle (full overclock breaks above ~9 atm).</summary>
  public float CornishEngineOverPressureMargin { get; set; } = 1.0f;

  /// <summary>Nominal maximum power a Cornish engine delivers (used to scale sub-machine output).</summary>
  public float CornishEngineMaxPower { get; set; } = 1.0f;

  /// <summary>Delivered power at full underclock (lowered) and full overclock (raised, can exceed nominal).</summary>
  public float CornishEngineUnderclockPower { get; set; } = 0.5f;
  public float CornishEngineOverclockPower { get; set; } = 1.25f;

  /// <summary>Steam (m³/s) a Cornish engine consumes per unit power (efficient — less volume, higher pressure).</summary>
  public float CornishEngineSteamPerPower { get; set; } = 2f;

  /// <summary>Seconds an engine may run above its band before it breaks and needs repairing.</summary>
  public float EngineOverPressureSeconds { get; set; } = 60f;

  /// <summary>Water (m³/s) the pump moves per unit engine power (one pump feeds ~4 Cornish or ~3 Lancashire boilers).</summary>
  public float PumpWaterPerPower { get; set; } = 0.09f;

  /// <summary>Maximum liquid pressure (atm) the pump sets at full power.</summary>
  public float PumpMaxPressure { get; set; } = 5f;
  #endregion

  #region Steam condenser
  /// <summary>Steam (m³/s) a condenser pulls from its steam line and condenses.</summary>
  public float CondenserSteamPerSecond { get; set; } = 3f;

  /// <summary>Litres of coolant water drawn from the north line per litre of condensed steam.</summary>
  public float CondenserCoolantRatio { get; set; } = 1.0f;
  #endregion
}

/// <summary>
/// Central, JSON-configurable access point for the mod's gameplay tunables. Call
/// <see cref="Load"/> once during mod startup; until then (and if the config fails to load) the
/// hard-coded defaults in <see cref="PpexConfig"/> apply. Use the members here everywhere in
/// code — e.g. <c>PpexValues.BoilingPoint</c>.
/// </summary>
public static class PpexValues
{
  /// <summary>Config file name, written under the game's <c>ModConfig</c> folder.</summary>
  public const string ConfigFileName = "ppex.json";

  private static PpexConfig _config = new();

  /// <summary>
  /// Loads <see cref="ConfigFileName"/> from the ModConfig folder (falling back to defaults if
  /// absent or invalid) and writes it back so the file is created on first run and gains any
  /// newly added keys on update.
  /// </summary>
  public static void Load(ICoreAPI api)
  {
    try
    {
      _config =
        api.LoadModConfig<PpexConfig>(ConfigFileName) ?? new PpexConfig();
    }
    catch (Exception e)
    {
      api.Logger.Error(
        "[ppex] Failed to read {0}; using defaults. {1}",
        ConfigFileName,
        e
      );
      _config = new PpexConfig();
    }

    try
    {
      api.StoreModConfig(_config, ConfigFileName);
    }
    catch (Exception e)
    {
      api.Logger.Warning("[ppex] Could not write {0}. {1}", ConfigFileName, e);
    }
  }

  #region Pipes
  public static float CopperPipeBurstPressure =>
    _config.CopperPipeBurstPressure;
  public static float BronzePipeBurstPressure =>
    _config.BronzePipeBurstPressure;
  public static float BrassPipeBurstPressure => _config.BrassPipeBurstPressure;
  public static float IronPipeBurstPressure => _config.IronPipeBurstPressure;
  public static float SteelPipeBurstPressure => _config.SteelPipeBurstPressure;
  public static float FerricGlowFactor => _config.FerricGlowFactor;
  public static float BoilingPoint => _config.BoilingPoint;
  public static float FerricPipeHeatLoss => _config.FerricPipeHeatLoss;
  public static float NonFerricPipeHeatLoss => _config.NonFerricPipeHeatLoss;
  public static float ChimneyVentRate => _config.ChimneyVentRate;
  public static float LiquidLeakRatePerOpening =>
    _config.LiquidLeakRatePerOpening;
  #endregion

  #region Boiler
  public static float ExhaustMaxOutputPressure =>
    _config.ExhaustMaxOutputPressure;
  public static float BoilerMaxOutputPressure =>
    _config.BoilerMaxOutputPressure;
  public static float BoilerOverpressureSeconds =>
    _config.BoilerOverpressureSeconds;
  public static float BoilerCoalBurnRateMultiplier =>
    _config.BoilerCoalBurnRateMultiplier;
  public static float BoilerMaxTemperature => _config.BoilerMaxTemperature;
  public static float BoilerHeatingSpeed => _config.BoilerHeatingSpeed;
  public static float BoilerThermalMassReference =>
    _config.BoilerThermalMassReference;
  public static float BoilerWaterCapacity => _config.BoilerWaterCapacity;
  public static float BoilerMaxInternalSteam => _config.BoilerMaxInternalSteam;
  public static float BoilerSteamPerSecond => _config.BoilerSteamPerSecond;
  public static float BoilerWaterPerSteam => _config.BoilerWaterPerSteam;
  public static int BoilerExplosionRadius => _config.BoilerExplosionRadius;
  public static float BoilerLidVentSpeed => _config.BoilerLidVentSpeed;
  public static float BoilerLidCoolTarget => _config.BoilerLidCoolTarget;
  public static float BoilerLidVentPressure => _config.BoilerLidVentPressure;
  #endregion

  #region Cornish boiler
  public static float CornishBoilerWaterCapacity =>
    _config.CornishBoilerWaterCapacity;
  public static float CornishBoilerMaxInternalSteam =>
    _config.CornishBoilerMaxInternalSteam;
  public static float CornishBoilerSteamPerSecond =>
    _config.CornishBoilerSteamPerSecond;
  public static float CornishBoilerMaxOutputPressure =>
    _config.CornishBoilerMaxOutputPressure;
  public static float CornishBoilerMaxTemperature =>
    _config.CornishBoilerMaxTemperature;
  #endregion

  #region Engines + sub-machines
  public static float WattEngineMinPressure => _config.WattEngineMinPressure;
  public static float WattEngineMaxPressure => _config.WattEngineMaxPressure;
  public static float WattEngineMaxPower => _config.WattEngineMaxPower;
  public static float WattEngineSteamPerPower =>
    _config.WattEngineSteamPerPower;
  public static float CornishEngineUnderclockPressure =>
    _config.CornishEngineUnderclockPressure;
  public static float CornishEngineOverclockPressure =>
    _config.CornishEngineOverclockPressure;
  public static float CornishEngineOverPressureMargin =>
    _config.CornishEngineOverPressureMargin;
  public static float CornishEngineMaxPower => _config.CornishEngineMaxPower;
  public static float CornishEngineUnderclockPower =>
    _config.CornishEngineUnderclockPower;
  public static float CornishEngineOverclockPower =>
    _config.CornishEngineOverclockPower;
  public static float CornishEngineSteamPerPower =>
    _config.CornishEngineSteamPerPower;
  public static float EngineOverPressureSeconds =>
    _config.EngineOverPressureSeconds;
  public static float PumpWaterPerPower => _config.PumpWaterPerPower;
  public static float PumpMaxPressure => _config.PumpMaxPressure;
  #endregion

  #region Steam condenser
  public static float CondenserSteamPerSecond =>
    _config.CondenserSteamPerSecond;
  public static float CondenserCoolantRatio => _config.CondenserCoolantRatio;
  #endregion
}

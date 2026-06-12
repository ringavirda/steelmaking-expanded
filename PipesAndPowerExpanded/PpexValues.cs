using System;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded;

/// <summary>
/// JSON-serializable gameplay tunables for Pipes and Power Expanded. Loaded from (and written
/// to) <c>ModConfig/ppex.json</c>; the property defaults below apply when the file is missing or
/// a key is absent. Accessed through <see cref="PpexValues"/>, not directly.
/// <para>
/// All gas/liquid volumes are in <b>litres</b> (matching vanilla liquid containers). Pressure is
/// a dimensionless ratio (volume / capacity), expressed in atm.
/// </para>
/// </summary>
public class PpexConfig
{
  #region Pipes
  /// <summary>Litres a single pipe holds at 1 atm (both the gas and water pools).</summary>
  public float LitresPerPipe { get; set; } = 30f;

  /// <summary>Per-material pipe burst pressure (atm) — the weakest pipe limits a run.</summary>
  public float IronPipeBurstPressure { get; set; } = 5.0f;
  public float SteelPipeBurstPressure { get; set; } = 10.0f;

  /// <summary>Temperature (°C) at which water boils into steam / steam condenses into water.</summary>
  public float BoilingPoint { get; set; } = 100f;

  /// <summary>Gas (L/s) a vanilla chimney draws from the network when capping the top
  /// connector of a passthrough / passthrough-bend / outlet block.</summary>
  public float ChimneyGasDrawRate { get; set; } = 16.0f;

  /// <summary>Gas (L/s) bled per open-ended pipe connector (leak) — only the volume above
  /// the network's 1 atm capacity is vented, so a leaking run can never build pressure.</summary>
  public float GasLeakRate { get; set; } = 8.0f;

  /// <summary>Seconds a pipe run may sit at its weakest pipe's burst pressure (nowhere to
  /// vent) before a pipe lets go — mirrors the boiler over-pressure grace.</summary>
  public float PipeOverpressureSeconds { get; set; } = 30f;

  /// <summary>Liquid (L/s) drained from the network per open-ended pipe connector (leak).</summary>
  public float LiquidLeakRate { get; set; } = 10.0f;

  /// <summary>Water (L) lost to natural evaporation per in-game day (boiler pool and pipe water pool). 100 L over 2 days = 50 L/day.</summary>
  public float EvaporationLitresPerDay { get; set; } = 50f;
  #endregion

  #region Steam
  /// <summary>Litres of steam produced by boiling one litre of water (and the reverse ratio steam condenses back at).</summary>
  public float SteamExpansionFactor { get; set; } = 16f;

  /// <summary>Steam temperature (°C) produced when the boiler is at its minimum operating water level (hottest).</summary>
  public float SteamTempLowWater { get; set; } = 220f;

  /// <summary>Steam temperature (°C) produced when the boiler is at its maximum operating water level (coolest).</summary>
  public float SteamTempHighWater { get; set; } = 160f;
  #endregion

  #region Boiler (base values used for the Lancashire variant)
  /// <summary>Max output-network pressure (atm) a boiler can vent exhaust into; above it the fire goes out.</summary>
  public float ExhaustMaxOutputPressure { get; set; } = 0.8f;

  /// <summary>Steam pressure (atm) the boiler chokes at — it stops pushing steam above this on the outlet network.</summary>
  public float BoilerMaxOutputPressure { get; set; } = 12.0f;

  /// <summary>Seconds a boiler may sit over <see cref="BoilerMaxOutputPressure"/> (still firing, nowhere to vent) before it explodes.</summary>
  public float BoilerOverpressureSeconds { get; set; } = 30f;

  /// <summary>Seconds of "heating up" (water present + coal lit) before the boiler starts boiling — blast-furnace style.</summary>
  public float BoilerHeatUpSeconds { get; set; } = 180f;

  /// <summary>Grace seconds the boiler keeps running after the fire dies (or water leaves range) before it shuts down.</summary>
  public float BoilerShutdownDelaySeconds { get; set; } = 10f;

  /// <summary>Internal steam (L/s) that condenses back to water once the boiler has shut down.</summary>
  public float BoilerShutdownCondenseRate { get; set; } = 200f;

  /// <summary>Total internal capacity (L) shared between water and steam.</summary>
  public float BoilerCapacity { get; set; } = 1200f;

  /// <summary>Minimum water (L) needed before the boiler will begin heating/boiling.</summary>
  public float BoilerMinBoilWater { get; set; } = 200f;

  /// <summary>Maximum water (L) the boiler will hold/boil — the rest of the capacity is reserved for steam.</summary>
  public float BoilerMaxBoilWater { get; set; } = 800f;

  /// <summary>Fraction of the vessel capacity the automatic pump intake fills to — keeps a piped
  /// supply from overfilling the boiler (manual pouring can still go up to the boil-water ceiling).</summary>
  public float BoilerWaterIntakeFillFraction { get; set; } = 0.5f;

  /// <summary>Steam (L/s) produced while boiling at full tilt (consumes this / <see cref="SteamExpansionFactor"/> litres of water).</summary>
  public float BoilerSteamPerSecond { get; set; } = 48f;

  /// <summary>Exhaust (L/s) a burning boiler vents into its exhaust network — fixed for every boiler variant.</summary>
  public float BoilerExhaustPerSecond { get; set; } = 16f;

  /// <summary>Seconds a boiler may sit choked (fire lit but its exhaust outlet backed up to the vent-pressure cap) before its fuel pile is snuffed out.</summary>
  public float BoilerChokeExtinguishSeconds { get; set; } = 10f;

  /// <summary>Block radius damaged when a boiler explodes.</summary>
  public int BoilerExplosionRadius { get; set; } = 4;

  /// <summary>A boiler burst shatters every block in its radius whose mining resistance is below
  /// this — the mod's pipes, ports, coal piles and soft terrain. Sturdier blocks ride it out;
  /// keep this under 45 so other boilers/engines (and their resistance-45 structure fillers) and
  /// reinforced stone survive intact.</summary>
  public float BoilerBlastResistanceThreshold { get; set; } = 20f;

  /// <summary>Fraction (0..1) of the boiler's construction materials scattered as salvage when it
  /// bursts — less forgiving than mining it intact (the JSON <c>brokenDropsRatio</c>).</summary>
  public float BoilerExplosionDropRatio { get; set; } = 0.4f;

  /// <summary>Internal steam (L/s) an open lid vents to atmosphere.</summary>
  public float BoilerLidVentRate { get; set; } = 200f;

  /// <summary>Internal steam (L/s) bled to atmosphere when the steam outlet has no pipe
  /// attached — the boiler's neck is open, so steam jets out instead of pressurising.</summary>
  public float BoilerSteamLeakRate { get; set; } = 16f;

  /// <summary>Rendered water-surface height (block units) while the boiler holds some water
  /// but is below its operating threshold — kept below the flue tubes.</summary>
  public float BoilerWaterSurfaceLowLevel { get; set; } = 0.2f;

  /// <summary>Rendered water-surface height (block units) once the boiler holds enough
  /// water to operate — raised above the flue tubes. Kept just under a full block to
  /// avoid z-fighting with the cell boundary.</summary>
  public float BoilerWaterSurfaceHighLevel { get; set; } = 0.99f;

  /// <summary>Extra steam (L) flashed per litre of admitted water per atm of feed-water
  /// pressure above 1 atm. Pumped, pressurized water raises a boiling boiler's steam
  /// pressure — the player must valve it down or risk a burst.</summary>
  public float WaterPressureSteamBoost { get; set; } = 1f;
  #endregion

  #region Cornish boiler
  /// <summary>Total internal capacity (L) of the Cornish boiler.</summary>
  public float CornishBoilerCapacity { get; set; } = 800f;

  /// <summary>Minimum water (L) the Cornish boiler needs to begin heating/boiling.</summary>
  public float CornishBoilerMinBoilWater { get; set; } = 150f;

  /// <summary>Maximum water (L) the Cornish boiler will hold/boil.</summary>
  public float CornishBoilerMaxBoilWater { get; set; } = 500f;

  /// <summary>Steam (L/s) the Cornish boiler produces while boiling at full tilt.</summary>
  public float CornishBoilerSteamPerSecond { get; set; } = 32f;

  /// <summary>Steam pressure (atm) the Cornish boiler chokes at — above the Watt engine's
  /// 4 atm break, so a pressure valve between boiler and engine is mandatory; the player
  /// manages the head so boilers don't burst and engines don't break.</summary>
  public float CornishBoilerMaxOutputPressure { get; set; } = 5.0f;

  public int CornishBoilerExplosionRadius { get; set; } = 3;
  #endregion

  #region Engines + sub-machines
  /// <summary>Inlet pressure (atm) at/above which the Watt engine runs.</summary>
  public float WattEngineEngagePressure { get; set; } = 2.0f;

  /// <summary>Inlet pressure (atm) above which the Watt engine wears toward a break.</summary>
  public float WattEngineBreakPressure { get; set; } = 4.0f;

  /// <summary>Power a Watt engine delivers while running.</summary>
  public float WattEngineMaxPower { get; set; } = 0.3f;

  /// <summary>Steam (L/s) a Watt engine consumes while running.</summary>
  public float WattEngineSteamRate { get; set; } = 30f;

  /// <summary>Hot condensed water (L/s) a Watt engine spits out its outlet while running.</summary>
  public float WattEngineWaterRate { get; set; } = 1f;

  /// <summary>Inlet pressure (atm) at/above which the Cornish engine runs, at the low / normal /
  /// high control-rod settings. The throttle raises the whole operating band: low works on a
  /// gentle 5–8 atm, normal on 6–8 atm, high demands a hot 7–8 atm.</summary>
  public float CornishEngineEngagePressureLow { get; set; } = 5.0f;
  public float CornishEngineEngagePressureNormal { get; set; } = 6.0f;
  public float CornishEngineEngagePressureHigh { get; set; } = 7.0f;

  /// <summary>Inlet pressure (atm) above which the Cornish engine wears toward a break, at the
  /// low / normal / high control-rod settings.</summary>
  public float CornishEngineBreakPressureLow { get; set; } = 8.0f;
  public float CornishEngineBreakPressureNormal { get; set; } = 8.0f;
  public float CornishEngineBreakPressureHigh { get; set; } = 8.0f;

  /// <summary>Nominal power the Cornish engine delivers at the normal control-rod setting (display reference).</summary>
  public float CornishEngineMaxPower { get; set; } = 1.0f;

  /// <summary>Cornish engine steam draw (L/s) at the low / normal / high control-rod settings.</summary>
  public float CornishEngineSteamLow { get; set; } = 8f;
  public float CornishEngineSteamNormal { get; set; } = 16f;
  public float CornishEngineSteamHigh { get; set; } = 32f;

  /// <summary>Cornish engine power at the low / normal / high control-rod settings.</summary>
  public float CornishEnginePowerLow { get; set; } = 0.2f;
  public float CornishEnginePowerNormal { get; set; } = 0.4f;
  public float CornishEnginePowerHigh { get; set; } = 0.8f;

  /// <summary>Cornish engine condensed-water output (L/s) at the low / normal / high settings.</summary>
  public float CornishEngineWaterLow { get; set; } = 0.3f;
  public float CornishEngineWaterNormal { get; set; } = 0.6f;
  public float CornishEngineWaterHigh { get; set; } = 1.2f;

  /// <summary>When the Cornish engine is overclocked (high throttle) its running sounds are
  /// scaled by these — louder strokes/hum and a lower, more violent gear growl. Normal and low
  /// settings are left at 1 (unchanged).</summary>
  public float CornishEngineOverclockVolume { get; set; } = 1.8f;
  public float CornishEngineOverclockPitch { get; set; } = 0.8f;

  /// <summary>Steam-engine efficiency: an engine sets its sub-machine's output pressure
  /// (pump water, air blower) to its inlet steam pressure times this fraction.</summary>
  public float SteamEngineEfficiency { get; set; } = 0.75f;

  /// <summary>Seconds an engine may run above its band before it breaks and needs repairing.</summary>
  public float EngineOverPressureSeconds { get; set; } = 60f;

  /// <summary>"Normal" mechanical-network speed an MP generator holds while its load is within the
  /// engine's rated capacity. Light loads can't push past it (the generator stops driving above
  /// this); heavier loads drag the network below it.</summary>
  public float MpRatedSpeed { get; set; } = 1.0f;

  /// <summary>MP consumer load (resistance) an engine's generator can hold at <see cref="MpRatedSpeed"/>
  /// per unit of the engine's power output. A Watt engine at full power (0.3) × this (1.667) = 0.5,
  /// which is four active helve hammers (0.125 resistance each) held at the rated speed. Adding load
  /// past the rated amount slows the network (constant power: speed = budget / load), and past double
  /// it drags the engine below half speed, where it overstresses and stops.</summary>
  public float MpLoadPerEnginePower { get; set; } = 0.875f;

  /// <summary>Water (L/s) the pump injects at full engine power (scales with the engine's
  /// power fraction, so a throttled or overclocked engine moves proportionally less/more).</summary>
  public float PumpWaterPerSecond { get; set; } = 5f;

  /// <summary>A fluid intake only draws water when the whole cube of this depth directly below it is water.</summary>
  public int FluidIntakeWaterDepth { get; set; } = 3;

  /// <summary>An intake is disabled if another intake sits within this many blocks (Euclidean), to stop players packing them.</summary>
  public float FluidIntakeExclusionRange { get; set; } = 6f;
  #endregion

  #region Steam condenser
  /// <summary>Steam (L/s) a condenser pulls from its (north) steam line and condenses.</summary>
  public float CondenserSteamPerSecond { get; set; } = 30f;

  /// <summary>Water (L/s) the condenser passes through its W↔E water line (the through-flow cap).</summary>
  public float CondenserWaterThroughput { get; set; } = 50f;
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
  public static float LitresPerPipe => _config.LitresPerPipe;
  public static float IronPipeBurstPressure => _config.IronPipeBurstPressure;
  public static float SteelPipeBurstPressure => _config.SteelPipeBurstPressure;
  public static float BoilingPoint => _config.BoilingPoint;
  public static float ChimneyGasDrawRate => _config.ChimneyGasDrawRate;
  public static float GasLeakRate => _config.GasLeakRate;
  public static float PipeOverpressureSeconds =>
    _config.PipeOverpressureSeconds;
  public static float LiquidLeakRate => _config.LiquidLeakRate;
  public static float EvaporationLitresPerDay =>
    _config.EvaporationLitresPerDay;
  #endregion

  #region Steam
  public static float SteamExpansionFactor => _config.SteamExpansionFactor;
  public static float SteamTempLowWater => _config.SteamTempLowWater;
  public static float SteamTempHighWater => _config.SteamTempHighWater;
  #endregion

  #region Boiler
  public static float ExhaustMaxOutputPressure =>
    _config.ExhaustMaxOutputPressure;
  public static float BoilerMaxOutputPressure =>
    _config.BoilerMaxOutputPressure;
  public static float BoilerOverpressureSeconds =>
    _config.BoilerOverpressureSeconds;
  public static float BoilerHeatUpSeconds => _config.BoilerHeatUpSeconds;
  public static float BoilerShutdownDelaySeconds =>
    _config.BoilerShutdownDelaySeconds;
  public static float BoilerShutdownCondenseRate =>
    _config.BoilerShutdownCondenseRate;
  public static float BoilerCapacity => _config.BoilerCapacity;
  public static float BoilerMinBoilWater => _config.BoilerMinBoilWater;
  public static float BoilerMaxBoilWater => _config.BoilerMaxBoilWater;
  public static float BoilerWaterIntakeFillFraction =>
    _config.BoilerWaterIntakeFillFraction;
  public static float BoilerSteamPerSecond => _config.BoilerSteamPerSecond;
  public static float BoilerExhaustPerSecond => _config.BoilerExhaustPerSecond;
  public static float BoilerChokeExtinguishSeconds =>
    _config.BoilerChokeExtinguishSeconds;
  public static int BoilerExplosionRadius => _config.BoilerExplosionRadius;
  public static float BoilerBlastResistanceThreshold =>
    _config.BoilerBlastResistanceThreshold;
  public static float BoilerExplosionDropRatio =>
    _config.BoilerExplosionDropRatio;
  public static float BoilerLidVentRate => _config.BoilerLidVentRate;
  public static float BoilerSteamLeakRate => _config.BoilerSteamLeakRate;
  public static float BoilerWaterSurfaceLowLevel =>
    _config.BoilerWaterSurfaceLowLevel;
  public static float BoilerWaterSurfaceHighLevel =>
    _config.BoilerWaterSurfaceHighLevel;
  public static float WaterPressureSteamBoost =>
    _config.WaterPressureSteamBoost;
  #endregion

  #region Cornish boiler
  public static float CornishBoilerCapacity => _config.CornishBoilerCapacity;
  public static float CornishBoilerMinBoilWater =>
    _config.CornishBoilerMinBoilWater;
  public static float CornishBoilerMaxBoilWater =>
    _config.CornishBoilerMaxBoilWater;
  public static float CornishBoilerSteamPerSecond =>
    _config.CornishBoilerSteamPerSecond;
  public static float CornishBoilerMaxOutputPressure =>
    _config.CornishBoilerMaxOutputPressure;
  public static int CornishBoilerExplosionRadius =>
    _config.CornishBoilerExplosionRadius;
  #endregion

  #region Engines + sub-machines
  public static float WattEngineEngagePressure =>
    _config.WattEngineEngagePressure;
  public static float WattEngineBreakPressure =>
    _config.WattEngineBreakPressure;
  public static float WattEngineMaxPower => _config.WattEngineMaxPower;
  public static float WattEngineSteamRate => _config.WattEngineSteamRate;
  public static float CornishEngineEngagePressureLow =>
    _config.CornishEngineEngagePressureLow;
  public static float CornishEngineEngagePressureNormal =>
    _config.CornishEngineEngagePressureNormal;
  public static float CornishEngineEngagePressureHigh =>
    _config.CornishEngineEngagePressureHigh;
  public static float CornishEngineBreakPressureLow =>
    _config.CornishEngineBreakPressureLow;
  public static float CornishEngineBreakPressureNormal =>
    _config.CornishEngineBreakPressureNormal;
  public static float CornishEngineBreakPressureHigh =>
    _config.CornishEngineBreakPressureHigh;
  public static float CornishEngineMaxPower => _config.CornishEngineMaxPower;
  public static float CornishEngineSteamLow => _config.CornishEngineSteamLow;
  public static float CornishEngineSteamNormal =>
    _config.CornishEngineSteamNormal;
  public static float CornishEngineSteamHigh => _config.CornishEngineSteamHigh;
  public static float CornishEnginePowerLow => _config.CornishEnginePowerLow;
  public static float CornishEnginePowerNormal =>
    _config.CornishEnginePowerNormal;
  public static float CornishEnginePowerHigh => _config.CornishEnginePowerHigh;
  public static float WattEngineWaterRate => _config.WattEngineWaterRate;
  public static float CornishEngineWaterLow => _config.CornishEngineWaterLow;
  public static float CornishEngineWaterNormal =>
    _config.CornishEngineWaterNormal;
  public static float CornishEngineWaterHigh => _config.CornishEngineWaterHigh;
  public static float CornishEngineOverclockVolume =>
    _config.CornishEngineOverclockVolume;
  public static float CornishEngineOverclockPitch =>
    _config.CornishEngineOverclockPitch;
  public static float SteamEngineEfficiency => _config.SteamEngineEfficiency;
  public static float EngineOverPressureSeconds =>
    _config.EngineOverPressureSeconds;
  public static float MpRatedSpeed => _config.MpRatedSpeed;
  public static float MpLoadPerEnginePower => _config.MpLoadPerEnginePower;
  public static float PumpWaterPerSecond => _config.PumpWaterPerSecond;
  public static int FluidIntakeWaterDepth => _config.FluidIntakeWaterDepth;
  public static float FluidIntakeExclusionRange =>
    _config.FluidIntakeExclusionRange;
  #endregion

  #region Steam condenser
  public static float CondenserSteamPerSecond =>
    _config.CondenserSteamPerSecond;
  public static float CondenserWaterThroughput =>
    _config.CondenserWaterThroughput;
  #endregion
}

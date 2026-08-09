using ExpandedLib.Registries.Config;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded;

/// <summary>
/// JSON-serializable gameplay tunables for Pipes and Power Expanded. Loaded from (and written
/// to) <c>ModConfig/ppex_values.json</c>; the property defaults below apply when the file is missing
/// or a key is absent (and any NaN/infinite/negative value is reset to its default on load). Accessed
/// through <see cref="PpexValues"/>, not directly.
/// <para>
/// All gas/liquid volumes are in <b>litres</b> (matching vanilla liquid containers). Pressure is
/// a dimensionless ratio (volume / capacity), expressed in atm.
/// </para>
/// </summary>
[ExConfigRegister(
  "ppex_values.json",
  "ppex",
  LegacyFileNames = new string[] { "ppex.json" },
  Manageable = true
)]
public class PpexConfig : IExVersionedConfig {
  /// <summary>Mod version that last wrote this file; drives the <see cref="Migrations"/> resets.
  /// Managed by <see cref="ExConfigRegister{TConfig}"/> - do not set by hand.</summary>
  public string? ConfigVersion { get; set; }

  /// <summary>
  /// Version-driven default resets. When a player upgrades across one of these versions the listed
  /// values are forced back to the defaults above, discarding their saved tuning for just those keys
  /// (everything else is preserved). Add an entry per release that rebalances values you want pushed
  /// out to existing configs; use <c>nameof</c> for the field names. An entry with no
  /// <c>ResetFields</c> resets the whole config.
  /// </summary>
  public static readonly ExConfigMigration[] Migrations =
  [
    // 0.6.0: the engine fluid pump now scales off absolute engine power, so its base
    // throughput was retuned (5 -> 16.67 L/s) - push the new default to existing configs.
    new() { ToVersion = "0.6.0", ResetFields = [nameof(PumpWaterPerSecond)] },
    // 0.6.5: the MP overstress ceiling now scales with every engine on the shaft, and the load an
    // engine holds per unit of power was raised (0.875 -> 1.37) so a steam plant is worth building
    // against an electrical generator - push the new default to existing configs.
    new()
    {
      ToVersion = "0.6.5",
      ResetFields = [nameof(MpLoadPerEnginePower)],
    },
    // 0.6.6: mining a machine intact now returns all of its construction materials (0.8 -> 1.0);
    // the salvage tax only discouraged rebuilding a plant. Bursting one is still penalised.
    new()
    {
      ToVersion = "0.6.6",
      ResetFields = [nameof(RccBrokenDropsRatio)],
    },
  ];

  #region Pipes
  /// <summary>Litres a single pipe holds at 1 atm (both the gas and water pools).</summary>
  [ExConfigRange(1, 1_000_000)] // pipe capacity divides pressure - must stay positive
  public float LitresPerPipe { get; set; } = 30f;

  /// <summary>Per-material pipe burst pressure (atm) - the weakest pipe limits a run.</summary>
  public float IronPipeBurstPressure { get; set; } = 5.0f;
  public float SteelPipeBurstPressure { get; set; } = 10.0f;

  /// <summary>Temperature (°C) at which water boils into steam / steam condenses into water.</summary>
  public float BoilingPoint { get; set; } = 100f;

  /// <summary>Gas (L/s) a vanilla chimney draws from the network when capping the top
  /// connector of a passthrough / passthrough-bend / outlet block.</summary>
  public float ChimneyGasDrawRate { get; set; } = 16.0f;

  /// <summary>Gas (L/s) bled per open-ended pipe connector (leak) - only the volume above
  /// the network's 1 atm capacity is vented, so a leaking run can never build pressure.</summary>
  public float GasLeakRate { get; set; } = 8.0f;

  /// <summary>Seconds a pipe run may sit at its weakest pipe's burst pressure (nowhere to
  /// vent) before a pipe lets go - mirrors the boiler over-pressure grace.</summary>
  public float PipeOverpressureSeconds { get; set; } = 30f;

  /// <summary>Liquid (L/s) drained from the network per open-ended pipe connector (leak).</summary>
  public float LiquidLeakRate { get; set; } = 10.0f;

  /// <summary>Water (L) lost to natural evaporation per in-game day (boiler pool and pipe water pool). 100 L over 2 days = 50 L/day.</summary>
  public float EvaporationLitresPerDay { get; set; } = 50f;
  #endregion

  #region Steam
  /// <summary>Litres of steam produced by boiling one litre of water (and the reverse ratio steam condenses back at).</summary>
  [ExConfigRange(1, 100_000)] // divides steam back into water - must stay positive
  public float SteamExpansionFactor { get; set; } = 16f;

  /// <summary>Exponent of the saturated-steam temperature curve: steam temperature (°C) =
  /// <see cref="BoilingPoint"/> × (gaugePressure + 1)^exponent (absolute pressure in atm). 0.25
  /// closely tracks the real water saturation curve (e.g. ~150°C at 4 atm gauge, ~186°C at 11 atm
  /// gauge; 100°C at 0 atm gauge).</summary>
  public float SteamSaturationExponent { get; set; } = 0.25f;
  #endregion

  #region Boiler (base values used for the Lancashire variant)
  /// <summary>Max output-network pressure (atm) a boiler can vent exhaust into; above it the fire goes out.</summary>
  public float ExhaustMaxOutputPressure { get; set; } = 0.8f;

  /// <summary>Steam pressure (atm) the boiler chokes at - it stops pushing steam above this on the outlet network.</summary>
  public float BoilerMaxOutputPressure { get; set; } = 12.0f;

  /// <summary>Seconds a boiler may sit over <see cref="BoilerMaxOutputPressure"/> (still firing, nowhere to vent) before it explodes.</summary>
  public float BoilerOverpressureSeconds { get; set; } = 30f;

  /// <summary>Seconds of "heating up" (water present + coal lit) before the boiler starts boiling - blast-furnace style.</summary>
  public float BoilerHeatUpSeconds { get; set; } = 180f;

  /// <summary>Grace seconds the boiler keeps running after the fire dies (or water leaves range) before it shuts down.</summary>
  public float BoilerShutdownDelaySeconds { get; set; } = 10f;

  /// <summary>Internal steam (L/s) that condenses back to water once the boiler has shut down.</summary>
  public float BoilerShutdownCondenseRate { get; set; } = 200f;

  /// <summary>Total internal capacity (L) shared between water and steam.</summary>
  public float BoilerCapacity { get; set; } = 1200f;

  /// <summary>Minimum water (L) needed before the boiler will begin heating/boiling.</summary>
  public float BoilerMinBoilWater { get; set; } = 200f;

  /// <summary>Maximum water (L) the boiler will hold/boil - the rest of the capacity is reserved for steam.</summary>
  public float BoilerMaxBoilWater { get; set; } = 800f;

  /// <summary>Fraction of the vessel capacity the automatic pump intake fills to - keeps a piped
  /// supply from overfilling the boiler (manual pouring can still go up to the boil-water ceiling).</summary>
  [ExConfigRange(0, 1)]
  public float BoilerWaterIntakeFillFraction { get; set; } = 0.5f;

  /// <summary>Maximum rate (L/s) the boiler draws water from its feed network through the automatic
  /// intake - the supply trickles in rather than slurping the whole headroom in one tick.</summary>
  public float BoilerWaterIntakeRate { get; set; } = 10f;

  /// <summary>Steam (L/s) produced while boiling at full tilt (consumes this / <see cref="SteamExpansionFactor"/> litres of water).</summary>
  public float BoilerSteamPerSecond { get; set; } = 48f;

  /// <summary>Exhaust (L/s) a burning boiler vents into its exhaust network - fixed for every boiler variant.</summary>
  public float BoilerExhaustPerSecond { get; set; } = 16f;

  /// <summary>Seconds a boiler may sit choked (fire lit but its exhaust outlet backed up to the vent-pressure cap) before its fuel pile is snuffed out.</summary>
  public float BoilerChokeExtinguishSeconds { get; set; } = 10f;

  /// <summary>Block radius damaged when a boiler explodes.</summary>
  public int BoilerExplosionRadius { get; set; } = 4;

  /// <summary>A boiler burst shatters every block in its radius below this resistance (pipes, ports,
  /// coal piles, soft terrain). Keep under 45 so other boilers/engines and their resistance-45
  /// fillers survive.</summary>
  public float BoilerBlastResistanceThreshold { get; set; } = 20f;

  /// <summary>Fraction (0..1) of the boiler's construction materials scattered as salvage when it
  /// bursts - less forgiving than mining it intact (see <see cref="RccBrokenDropsRatio"/>).</summary>
  [ExConfigRange(0, 1)]
  public float BoilerExplosionDropRatio { get; set; } = 0.4f;

  /// <summary>Fraction (0..1) of an engine/boiler's construction materials recovered when it is mined
  /// intact - the right-click-construction salvage ratio. Full recovery by default: taking a machine
  /// down to move it is a normal part of laying out a plant, and a tax on that only discourages
  /// rebuilding. Bursting one is still penalised, through <see cref="BoilerExplosionDropRatio"/>.
  /// Player-tunable; applied live on the next break.</summary>
  [ExConfigRange(0, 1)]
  public float RccBrokenDropsRatio { get; set; } = 1.0f;

  /// <summary>Internal steam (L/s) an open lid vents to atmosphere.</summary>
  public float BoilerLidVentRate { get; set; } = 200f;

  /// <summary>Internal steam (L/s) bled to atmosphere when the steam outlet has no pipe
  /// attached - the boiler's neck is open, so steam jets out instead of pressurising.</summary>
  public float BoilerSteamLeakRate { get; set; } = 16f;

  /// <summary>Rendered water-surface height (block units) while the boiler holds some water
  /// but is below its operating threshold - kept below the flue tubes.</summary>
  [ExConfigRange(0, 1)]
  public float BoilerWaterSurfaceLowLevel { get; set; } = 0.2f;

  /// <summary>Rendered water-surface height (block units) once the boiler holds enough
  /// water to operate - raised above the flue tubes. Kept just under a full block to
  /// avoid z-fighting with the cell boundary.</summary>
  [ExConfigRange(0, 1)]
  public float BoilerWaterSurfaceHighLevel { get; set; } = 0.99f;

  /// <summary>Extra steam (L) flashed per litre of admitted water per atm of feed-water pressure
  /// above 1 atm - pumped water raises a boiling boiler's steam pressure toward a burst.</summary>
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

  /// <summary>Steam pressure (atm) the Cornish boiler chokes at - above the Watt engine's 4 atm
  /// break, so a pressure valve between boiler and engine is mandatory.</summary>
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
  /// gentle 5-8 atm, normal on 6-8 atm, high demands a hot 7-8 atm.</summary>
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
  /// scaled by these - louder strokes/hum and a lower, more violent gear growl. Normal and low
  /// settings are left at 1 (unchanged).</summary>
  public float CornishEngineOverclockVolume { get; set; } = 1.8f;
  public float CornishEngineOverclockPitch { get; set; } = 0.8f;

  /// <summary>Steam-engine efficiency: an engine sets its sub-machine's output pressure
  /// (pump water, air blower) to its inlet steam pressure times this fraction.</summary>
  [ExConfigRange(0, 1)]
  public float SteamEngineEfficiency { get; set; } = 0.75f;

  /// <summary>Seconds an engine may run above its band before it breaks and needs repairing.</summary>
  public float EngineOverPressureSeconds { get; set; } = 60f;

  /// <summary>"Normal" MP-network speed a generator holds while its load is within the engine's
  /// rated capacity; light loads can't push past it, heavier loads drag it below.</summary>
  public float MpRatedSpeed { get; set; } = 1.0f;

  /// <summary>MP load an engine's generator holds at <see cref="MpRatedSpeed"/> per unit of engine
  /// power. A Watt at full power (0.3) × this = ~0.41 = three helve hammers. Load past the rated
  /// amount slows the network (speed = budget / load); past double it the engine stalls and stops.
  /// Raised from 0.875 so a steam plant is worth building against an electrical generator: three
  /// Cornish engines on a shared shaft now reach roughly 500 W where they made about 320 W.</summary>
  public float MpLoadPerEnginePower { get; set; } = 1.37f;

  /// <summary>Water (L/s) the engine fluid pump moves per unit of mechanical power (Watt 0.3 → 5 L/s,
  /// Cornish 0.2/0.4/0.8 → 3.3/6.7/13.3 L/s).</summary>
  public float PumpWaterPerSecond { get; set; } = 16.67f;

  /// <summary>Water (L/s) the manual (hand-cranked) fluid pump transfers from its intake line to
  /// its output line at a fixed 1 atm - a manual boiler-startup feed, slower than the engine pump.</summary>
  public float ManualPumpWaterPerSecond { get; set; } = 2f;

  /// <summary>Water (L/s) the mechanical fluid pump moves at or above <see cref="MpPumpMaxSpeed"/>.
  /// Between the hand crank and the engine pump: it runs off a line shaft, so it can fill a boiler
  /// that has no engine of its own yet.</summary>
  public float MpPumpWaterPerSecond { get; set; } = 8f;

  /// <summary>Delivery head (atm) the mechanical fluid pump commands, independent of axle speed -
  /// the walking beam lifts the same column however fast it runs.</summary>
  public float MpPumpDeliveryPressure { get; set; } = 1.5f;

  /// <summary>Axle speed at or below which the mechanical fluid pump moves nothing.</summary>
  public float MpPumpMinSpeed { get; set; } = 0.5f;

  /// <summary>Axle speed at or above which the mechanical fluid pump moves its full rate.</summary>
  public float MpPumpMaxSpeed { get; set; } = 1.5f;

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

  #region Recipe balance
  /// <summary>Active steam-machine recipe cost level - <c>"normal"</c> or <c>"cheap"</c>. Toggled
  /// in-game by <c>/exmod steam &lt;level&gt;</c>; the per-recipe numbers live in the separate
  /// <c>ppex_recipes.json</c> catalogue. Applied on the next world reload.</summary>
  public string RecipeLevel { get; set; } = "normal";
  #endregion
}

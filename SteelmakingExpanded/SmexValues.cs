using System;
using Vintagestory.API.Common;

namespace SteelmakingExpanded;

/// <summary>
/// JSON-serializable gameplay tunables for Steelmaking Expanded — the "magic
/// numbers" that balance the machines and the molten/gas systems. Loaded from
/// (and written to) <c>ModConfig/smex.json</c>; the property defaults below are
/// used when the file is missing or a key is absent. Accessed through
/// <see cref="SmexValues"/>, not directly.
/// </summary>
public class SmexConfig
{
  #region Molten system
  /// <summary>Temperature cooldown speed applied to molten-metal stacks held by the molten system (canal cells, taps, barrels, molds, the bessemer charge).</summary>
  public float MoltenCooldownSpeed { get; set; } = 24f;

  /// <summary>Max metal (units) that flows across one canal-to-canal connection per second. Higher = metal races down the run before it can cool; balance against <see cref="MoltenCooldownSpeed"/>.</summary>
  public int MoltenFlowRate { get; set; } = 50;

  /// <summary>Minimum metal (units) that must be able to move across a canal-to-canal connection for any flow to happen that tick. Stops sub-unit dribbles and keeps cells from endlessly equalising by tiny amounts.</summary>
  public int MoltenMinFlowAmount { get; set; } = 10;

  /// <summary>Default per-canal-block capacity (units) when a block sets no <c>maxUnits</c> attribute.</summary>
  public int CanalDefaultUnitCapacity { get; set; } = 50;

  /// <summary>Default canal-tap network drain speed (units/s) when no <c>drainSpeed</c> attribute is set.</summary>
  public float CanalDefaultDrainSpeed { get; set; } = 20f;

  /// <summary>Default large-mold capacity (units) when the mold sets no <c>requiredUnits</c> attribute.</summary>
  public int MoldDefaultUnits { get; set; } = 100;

  /// <summary>Default molten-barrel capacity (units) when no <c>maxUnits</c> attribute is set.</summary>
  public int BarrelDefaultMaxUnits { get; set; } = 800;

  /// <summary>Fire-clay consumed to seal a straight canal into a separator.</summary>
  public int CanalSealClayCost { get; set; } = 4;

  /// <summary>Fire-clay refunded when breaking a canal seal.</summary>
  public int CanalUnsealClayRefund { get; set; } = 2;
  #endregion

  #region Blastmix
  /// <summary>Blast-mix units that must be loaded into the hearth before the furnace can fire.</summary>
  public int BlastMixRequiredToFire { get; set; } = 320;

  /// <summary>Burn time (seconds) granted by a blast-mix charge burning in a coal pile.</summary>
  public int BlastmixBurnTime { get; set; } = 300;
  #endregion

  #region Bessemer converter
  /// <summary>Seconds the pour/fill lever must be held before the converter commits the action.</summary>
  public float BessemerPourHoldSeconds { get; set; } = 1f;

  /// <summary>Rusty gears consumed to spawn the converter vessel.</summary>
  public int BessemerRequiredGears { get; set; } = 4;

  /// <summary>Iron/steel rods consumed to spawn the converter vessel.</summary>
  public int BessemerRequiredRods { get; set; } = 12;
  #endregion

  #region Air blower / blast
  /// <summary>Pressure (atm) at or above which air in a pipe network counts as "blast".</summary>
  public float BlastPressureThreshold { get; set; } = 2.5f;

  /// <summary>Air (L/s) the air blower injects at full engine power (scales with the
  /// engine's power fraction). Output pressure tracks the engine's inlet steam pressure
  /// × <see cref="PipesAndPowerExpanded.PpexValues.SteamEngineEfficiency"/>.</summary>
  public float AirBlowerOutputPerSecond { get; set; } = 16f;
  #endregion

  #region Player safety
  /// <summary>Minimum mold-content temperature (°C) that burns a bare-handed player carrying it.</summary>
  public float MoldBurnMinTemperature { get; set; } = 200f;
  #endregion

  #region Cowper stove
  /// <summary>Cap (°C) on the cowper stove's internal regenerator temperature.</summary>
  public float CowperMaxTemperature { get; set; } = 1240f;

  /// <summary>Per-second heat-soak rate when an anthracite coal pile burns below the stove.</summary>
  public float CowperHeatingSpeedAnthracite { get; set; } = 0.0064f;

  /// <summary>Per-second heat-soak rate when a non-anthracite coal pile burns below the stove.</summary>
  public float CowperHeatingSpeedOtherCoal { get; set; } = 0.0048f;

  /// <summary>Per-second heat-soak rate with no coal pile below the stove.</summary>
  public float CowperHeatingSpeedDefault { get; set; } = 0.0012f;

  /// <summary>Per-second rate the soaked-up exhaust gives its heat to the regenerator.</summary>
  public float CowperCoolingSpeedExhaust { get; set; } = 0.3f;

  /// <summary>Per-second rate the regenerator loses heat into the air it reheats into hot blast.</summary>
  public float CowperCoolingSpeedAir { get; set; } = 0.0012f;

  /// <summary>Gas (L/s) the cowper stove draws each tick from each of its intakes — the furnace exhaust it soaks heat from, and the air it reheats into hot blast.</summary>
  public float CowperIntakeVolume { get; set; } = 24f;
  #endregion

  #region Blast furnace
  /// <summary>Maximum hearth temperature (°C) without a hot-blast boost.</summary>
  public float BfNaturalMaxTemp { get; set; } = 1420f;

  /// <summary>Maximum hearth temperature (°C) when fed hot blast above the boost threshold.</summary>
  public float BfBoostedMaxTemp { get; set; } = 1740f;

  /// <summary>Hot-blast temperature (°C) at or above which the furnace reaches its boosted max temp.</summary>
  public float BfBlastBoostThreshold { get; set; } = 800f;

  /// <summary>Temperature (°C) the hearth must reach (and hold) to start melting iron.</summary>
  public float BfIronMeltingPoint { get; set; } = 1482f;

  /// <summary>Maximum molten iron (units) the furnace can hold before stalling.</summary>
  public float BfMaxMoltenIron { get; set; } = 2400f;

  /// <summary>Maximum molten slag (units) the furnace can hold before stalling.</summary>
  public float BfMaxMoltenSlag { get; set; } = 600f;

  /// <summary>Seconds a fired furnace burns before it extinguishes.</summary>
  public int BfMaxFuelBurnTime { get; set; } = 1200;

  /// <summary>Seconds above the melting point before the furnace transitions to the melting phase.</summary>
  public float BfMeltStartDelay { get; set; } = 300f;

  /// <summary>Seconds between melt cycles while melting.</summary>
  public float BfMeltIntervalSec { get; set; } = 10f;

  /// <summary>Molten iron (units) produced per melt cycle.</summary>
  public float BfIronPerMeltCycle { get; set; } = 60f;

  /// <summary>Molten slag (units) produced per melt cycle.</summary>
  public float BfSlagPerMeltCycle { get; set; } = 10f;

  /// <summary>Blast-mix consumed per melt cycle.</summary>
  public int BfBlastMixPerMeltCycle { get; set; } = 16;

  /// <summary>Air/blast (L/s) the blast furnace draws through each tuyere.</summary>
  public float TuyereIntakeVolume { get; set; } = 12f;
  #endregion

  #region Bessemer converter
  /// <summary>Molten-metal capacity (units) of the converter vessel.</summary>
  public int BessemerConverterCapacity { get; set; } = 1200;

  /// <summary>Blast (L/s) the converter draws from its gas intake while refining.</summary>
  public float BessemerBlastPerSecond { get; set; } = 8.0f;

  /// <summary>Seconds of blast a charge needs to refine iron into steel.</summary>
  public float BessemerProcessDuration { get; set; } = 300f;

  /// <summary>Temperature (°C) the blast holds the charge at while refining.</summary>
  public float BessemerProcessTemperature { get; set; } = 1800f;

  /// <summary>Minimum geared mechanical speed for the converter to count as powered.</summary>
  public float BessemerPowerSpeedThreshold { get; set; } = 0.1f;
  #endregion

  #region Hopper bell (blast-mix maker)
  /// <summary>Items the hopper magazine can buffer.</summary>
  public int HopperMaxMagazineCapacity { get; set; } = 48;

  /// <summary>Iron ore consumed per blast-mix batch.</summary>
  public int HopperIronOreRequired { get; set; } = 12;

  /// <summary>Coke consumed per blast-mix batch.</summary>
  public int HopperCokeRequired { get; set; } = 3;

  /// <summary>Lime consumed per blast-mix batch.</summary>
  public int HopperLimeRequired { get; set; } = 1;

  /// <summary>Blast-mix produced per batch.</summary>
  public int HopperBlastmixProduced { get; set; } = 16;

  /// <summary>Blast-mix dropped per output pulse.</summary>
  public int HopperDropAmount { get; set; } = 4;
  #endregion

  #region Smoke stack
  /// <summary>Exhaust gas (L/s) the smoke stack vents from the network.</summary>
  public float SmokestackGasIntakeVolume { get; set; } = 48.0f;
  #endregion
}

/// <summary>
/// Central, JSON-configurable access point for the mod's gameplay tunables. Call
/// <see cref="Load"/> once during mod startup; until then (and if the config
/// fails to load) the hard-coded defaults in <see cref="SmexConfig"/> apply.
/// Use the members here everywhere in code — e.g. <c>SmexValues.MoltenCooldownSpeed</c>.
/// </summary>
public static class SmexValues
{
  /// <summary>Config file name, written under the game's <c>ModConfig</c> folder.</summary>
  public const string ConfigFileName = "smex.json";

  private static SmexConfig _config = new();

  /// <summary>
  /// Loads <see cref="ConfigFileName"/> from the ModConfig folder (falling back to
  /// defaults if absent or invalid) and writes it back so the file is created on
  /// first run and gains any newly added keys on update. Safe to call on either
  /// side; each side reads its own local copy.
  /// </summary>
  public static void Load(ICoreAPI api)
  {
    try
    {
      _config =
        api.LoadModConfig<SmexConfig>(ConfigFileName) ?? new SmexConfig();
    }
    catch (Exception e)
    {
      api.Logger.Error(
        "[smex] Failed to read {0}; using defaults. {1}",
        ConfigFileName,
        e
      );
      _config = new SmexConfig();
    }

    // Persist defaults / fill in keys added since the file was authored.
    try
    {
      api.StoreModConfig(_config, ConfigFileName);
    }
    catch (Exception e)
    {
      api.Logger.Warning("[smex] Could not write {0}. {1}", ConfigFileName, e);
    }
  }

  #region Molten system
  public static float MoltenCooldownSpeed => _config.MoltenCooldownSpeed;
  public static int MoltenFlowRate => _config.MoltenFlowRate;
  public static int MoltenMinFlowAmount => _config.MoltenMinFlowAmount;
  public static int CanalDefaultUnitCapacity =>
    _config.CanalDefaultUnitCapacity;
  public static float CanalDefaultDrainSpeed => _config.CanalDefaultDrainSpeed;
  public static int MoldDefaultUnits => _config.MoldDefaultUnits;
  public static int BarrelDefaultMaxUnits => _config.BarrelDefaultMaxUnits;
  public static int CanalSealClayCost => _config.CanalSealClayCost;
  public static int CanalUnsealClayRefund => _config.CanalUnsealClayRefund;
  #endregion

  #region Blast furnace / fuel
  public static int BlastMixRequiredToFire => _config.BlastMixRequiredToFire;
  public static int BlastmixBurnTime => _config.BlastmixBurnTime;
  #endregion

  #region Bessemer converter
  public static float BessemerPourHoldSeconds =>
    _config.BessemerPourHoldSeconds;
  public static int BessemerRequiredGears => _config.BessemerRequiredGears;
  public static int BessemerRequiredRods => _config.BessemerRequiredRods;
  #endregion

  #region Air blower / blast
  public static float BlastPressureThreshold => _config.BlastPressureThreshold;
  public static float AirBlowerOutputPerSecond =>
    _config.AirBlowerOutputPerSecond;
  #endregion

  #region Player safety
  public static float MoldBurnMinTemperature => _config.MoldBurnMinTemperature;
  #endregion

  #region Cowper stove
  public static float CowperMaxTemperature => _config.CowperMaxTemperature;
  public static float CowperHeatingSpeedAnthracite =>
    _config.CowperHeatingSpeedAnthracite;
  public static float CowperHeatingSpeedOtherCoal =>
    _config.CowperHeatingSpeedOtherCoal;
  public static float CowperHeatingSpeedDefault =>
    _config.CowperHeatingSpeedDefault;
  public static float CowperCoolingSpeedExhaust =>
    _config.CowperCoolingSpeedExhaust;
  public static float CowperCoolingSpeedAir => _config.CowperCoolingSpeedAir;
  public static float CowperIntakeVolume => _config.CowperIntakeVolume;
  #endregion

  #region Blast furnace
  public static float BfNaturalMaxTemp => _config.BfNaturalMaxTemp;
  public static float BfBoostedMaxTemp => _config.BfBoostedMaxTemp;
  public static float BfBlastBoostThreshold => _config.BfBlastBoostThreshold;
  public static float BfIronMeltingPoint => _config.BfIronMeltingPoint;
  public static float BfMaxMoltenIron => _config.BfMaxMoltenIron;
  public static float BfMaxMoltenSlag => _config.BfMaxMoltenSlag;
  public static int BfMaxFuelBurnTime => _config.BfMaxFuelBurnTime;
  public static float BfMeltStartDelay => _config.BfMeltStartDelay;
  public static float BfMeltIntervalSec => _config.BfMeltIntervalSec;
  public static float BfIronPerMeltCycle => _config.BfIronPerMeltCycle;
  public static float BfSlagPerMeltCycle => _config.BfSlagPerMeltCycle;
  public static int BfBlastMixPerMeltCycle => _config.BfBlastMixPerMeltCycle;
  public static float TuyereIntakeVolume => _config.TuyereIntakeVolume;
  #endregion

  #region Bessemer converter
  public static int BessemerConverterCapacity =>
    _config.BessemerConverterCapacity;
  public static float BessemerBlastPerSecond => _config.BessemerBlastPerSecond;
  public static float BessemerProcessDuration =>
    _config.BessemerProcessDuration;
  public static float BessemerProcessTemperature =>
    _config.BessemerProcessTemperature;
  public static float BessemerPowerSpeedThreshold =>
    _config.BessemerPowerSpeedThreshold;
  #endregion

  #region Hopper bell (blast-mix maker)
  public static int HopperMaxMagazineCapacity =>
    _config.HopperMaxMagazineCapacity;
  public static int HopperIronOreRequired => _config.HopperIronOreRequired;
  public static int HopperCokeRequired => _config.HopperCokeRequired;
  public static int HopperLimeRequired => _config.HopperLimeRequired;
  public static int HopperBlastmixProduced => _config.HopperBlastmixProduced;
  public static int HopperDropAmount => _config.HopperDropAmount;
  #endregion

  #region Smoke stack
  public static float SmokestackGasIntakeVolume =>
    _config.SmokestackGasIntakeVolume;
  #endregion
}

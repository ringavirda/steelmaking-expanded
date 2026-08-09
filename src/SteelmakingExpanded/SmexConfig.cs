using ExpandedLib.Registries.Config;
using Vintagestory.API.Common;

namespace SteelmakingExpanded;

/// <summary>
/// JSON-serializable gameplay tunables for Steelmaking Expanded - the "magic
/// numbers" that balance the machines and the molten/gas systems. Loaded from
/// (and written to) <c>ModConfig/smex_values.json</c>; the property defaults below
/// are used when the file is missing or a key is absent (and any NaN/infinite/negative
/// value is reset to its default on load). Accessed through <see cref="SmexValues"/>, not directly.
/// </summary>
[ExConfigRegister(
  "smex_values.json",
  "smex",
  LegacyFileNames = new string[] { "smex.json" },
  Manageable = true
)]
public class SmexConfig : IExVersionedConfig {
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
    // 0.9.0: the bessemer blast draw (1 -> 8 L/s) and smoke-stack vent rate (4 -> 48 L/s)
    // were retuned during the gas-volume rebalance - push the new defaults to pre-0.9.0
    // configs (e.g. players coming from 0.8.6). FromVersion null so even unversioned
    // files are caught; 0.9.0+ already carry these values.
    new()
    {
      ToVersion = "0.9.0",
      ResetFields =
      [
        nameof(BessemerBlastPerSecond),
        nameof(SmokestackGasIntakeVolume),
      ],
    },
    // 0.9.2: the converter vessel now costs a single smithable large gear (8 rods)
    // instead of 4 rusty gears (12 rods), and the air blower now scales off absolute
    // engine power so its base output was retuned (16 -> 48 L/s) - push the rebalanced
    // defaults to existing configs.
    new()
    {
      ToVersion = "0.9.2",
      ResetFields =
      [
        nameof(BessemerRequiredGears),
        nameof(BessemerRequiredRods),
        nameof(AirBlowerOutputPerSecond),
      ],
    },
    // 0.9.5: the blast furnace takes coke directly instead of crushed coke, so the per-batch coke
    // cost is restated in whole coke (3 crushed = 1.5 coke -> 2). Also pushes the raised exhaust
    // output pressure, without which the relief valve in the setup diagrams can never open.
    new()
    {
      ToVersion = "0.9.5",
      ResetFields =
      [
        nameof(HopperCokeRequired),
        nameof(BfExhaustOutputPressure),
      ],
    },
    // 0.9.6: breaking the converter now returns all of its construction materials (0.8 -> 1.0);
    // the salvage tax only discouraged rebuilding a plant.
    new()
    {
      ToVersion = "0.9.6",
      ResetFields = [nameof(RccBrokenDropsRatio)],
    },
  ];

  #region Molten system
  /// <summary>Temperature cooldown speed applied to molten-metal stacks held by the molten system (canal cells, taps, barrels, molds, the bessemer charge).</summary>
  public float MoltenCooldownSpeed { get; set; } = 24f;

  /// <summary>Multiplier on <see cref="MoltenCooldownSpeed"/> for metal stored in a standalone molten
  /// barrel (mirrors the converter's <see cref="BessemerCooldownCoefficient"/>). Below 1 the barrel
  /// holds its heat longer; 1 = the base molten rate. Applied live to metal already in the barrel.</summary>
  public float BarrelCooldownCoefficient { get; set; } = 1f;

  /// <summary>Multiplier on <see cref="MoltenCooldownSpeed"/> for metal cast in a mold parked under a
  /// canal tap. Below 1 the cast holds its heat longer; 1 = the base molten rate. Applied live.</summary>
  public float TapMoldCooldownCoefficient { get; set; } = 1f;

  /// <summary>Multiplier on <see cref="MoltenCooldownSpeed"/> for metal cast in a mold on a pedestal.
  /// Below 1 the cast holds its heat longer; 1 = the base molten rate. Applied live.</summary>
  public float MoldPedestalCooldownCoefficient { get; set; } = 1f;

  /// <summary>Max metal (units) flowing across one canal connection per second; balance against <see cref="MoltenCooldownSpeed"/>.</summary>
  public int MoltenFlowRate { get; set; } = 50;

  /// <summary>Minimum metal (units) that must move across a canal connection for any flow that tick (stops sub-unit dribbles).</summary>
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

  /// <summary>Large gears consumed to spawn the converter vessel.</summary>
  public int BessemerRequiredGears { get; set; } = 1;

  /// <summary>Iron/steel rods consumed to spawn the converter vessel.</summary>
  public int BessemerRequiredRods { get; set; } = 8;
  #endregion

  #region Air blower / blast
  /// <summary>
  /// Pressure (atm) at or above which air counts as blast for the Bessemer converter. Only a steam
  /// blower reaches it: it sits above <see cref="MpBlowerMaxPressure"/>, so a mechanically blown
  /// shop can make iron but not steel.
  /// </summary>
  public float BlastPressureThreshold { get; set; } = 2.5f;

  /// <summary>Air (L/s) the blower injects per unit of engine power (Cornish 0.2/0.4/0.8 →
  /// 9.6/19.2/38.4 L/s, Watt 0.3 → 14.4 L/s); output pressure tracks the engine's inlet steam ×
  /// <see cref="PipesAndPowerExpanded.PpexValues.SteamEngineEfficiency"/>.</summary>
  public float AirBlowerOutputPerSecond { get; set; } = 48f;
  #endregion

  #region Mechanical blower
  /// <summary>Air (L/s) the twin-tub blower delivers at or above <see cref="MpBlowerMaxSpeed"/>.
  /// A vanilla waterwheel turns at roughly speed 1, that is half rated output, which still covers
  /// the two tuyeres of one blast furnace.</summary>
  public float MpBlowerOutputPerSecond { get; set; } = 60f;

  /// <summary>
  /// Pressure ceiling (atm) the twin-tub blower raises its run to. Above
  /// <see cref="BfBlastPressureThreshold"/> so bellows run a blast furnace, below
  /// <see cref="BlastPressureThreshold"/> so they can never blow the converter.
  /// </summary>
  public float MpBlowerMaxPressure { get; set; } = 2.0f;

  /// <summary>Axle speed at or below which the bellows deliver nothing.</summary>
  public float MpBlowerMinSpeed { get; set; } = 0.5f;

  /// <summary>Axle speed at or above which the bellows deliver full output.</summary>
  public float MpBlowerMaxSpeed { get; set; } = 1.5f;
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

  /// <summary>Gas (L/s) the cowper stove draws each tick from each of its intakes - the furnace exhaust it soaks heat from, and the air it reheats into hot blast.</summary>
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

  /// <summary>
  /// Pressure ceiling the furnace pushes its exhaust to. This is what the flue can build against a
  /// sealed main, so it also sets how much headroom the run has before it saturates and the furnace
  /// stalls. At the old implicit 1 atm the pressure-relief valve in the setup diagrams could never
  /// open, since its own gate defaults to 1 atm. Well under the 5 atm iron / 10 atm steel burst.
  /// A run with any open end is clamped back to 1 atm regardless.
  /// </summary>
  public float BfExhaustOutputPressure { get; set; } = 2.0f;

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

  /// <summary>
  /// Pressure (atm) the air at a tuyere must reach before it counts as blast and the furnace will
  /// fire. Separate from the converter's <see cref="BlastPressureThreshold"/>: iron is gated low
  /// enough for a mechanical blower to reach, steel is not.
  /// </summary>
  public float BfBlastPressureThreshold { get; set; } = 1.5f;
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

  /// <summary>Multiplier on the converter charge's cooldown speed (vs <see cref="MoltenCooldownSpeed"/>).
  /// Below 1 the bath holds its heat longer, giving the player more time to pour before it solidifies
  /// (0.5 ⇒ half the molten-system rate, i.e. cools twice as slowly).</summary>
  public float BessemerCooldownCoefficient { get; set; } = 0.5f;

  /// <summary>Fraction of the converter capacity below which a hardened (cooled) charge can be chiselled
  /// out of the vessel instead of breaking the whole structure. A residue at or above this is salvaged
  /// by breaking it.</summary>
  [ExConfigRange(0, 1)]
  public float BessemerChiselMaxFraction { get; set; } = 0.2f;

  /// <summary>Fraction (0..1) of the converter's construction materials recovered when its vessel is
  /// broken - the right-click-construction salvage ratio. Full recovery by default: taking a machine
  /// down to move it is a normal part of laying out a plant, and a tax on that only discourages
  /// rebuilding. Player-tunable; applied live on the next break.</summary>
  [ExConfigRange(0, 1)]
  public float RccBrokenDropsRatio { get; set; } = 1.0f;
  #endregion

  #region Hopper bell (blast-mix maker)
  /// <summary>Items the hopper magazine can buffer.</summary>
  public int HopperMaxMagazineCapacity { get; set; } = 48;

  /// <summary>Iron ore consumed per blast-mix batch.</summary>
  public int HopperIronOreRequired { get; set; } = 12;

  /// <summary>Coke consumed per blast-mix batch. Whole coke: the furnace no longer takes the
  /// crushed intermediate, and 3 crushed coke was 1.5 coke at the old 1:2 crush ratio.</summary>
  public int HopperCokeRequired { get; set; } = 2;

  /// <summary>
  /// Charcoal consumed per blast-mix batch when the hopper is fed charcoal instead of coke.
  /// Charcoal carries roughly half the carbon of coke, so it takes twice as many pieces; the two
  /// fuels mix freely within one batch at that exchange rate.
  /// </summary>
  public int HopperCharcoalRequired { get; set; } = 4;

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

  #region Tool molds
  // Availability of the mod's added casting molds. Disabling one removes its clay-forming recipe
  // and hides it from creative/the handbook on the next world load, and stops any already-placed
  // mold of that type from yielding a casting immediately. Toggled in-game by a server admin via
  // /exmod molds <plate|ingot|rod|all> <on|off>; persisted to smex_values.json.

  /// <summary>Whether the plate mold (casts metal plates) is available.</summary>
  public bool EnablePlateMold { get; set; } = true;

  /// <summary>Whether the double-ingot mold (casts 2 ingots) is available.</summary>
  public bool EnableIngotMold { get; set; } = true;

  /// <summary>Whether the quad-rod mold (casts 4 rods) is available.</summary>
  public bool EnableRodMold { get; set; } = true;
  #endregion

  #region Recipe balance
  /// <summary>Active steelmaking recipe cost level - <c>"normal"</c> or <c>"cheap"</c>. Toggled
  /// in-game by <c>/exmod steel &lt;level&gt;</c>; the per-recipe numbers live in the separate
  /// <c>smex_recipes.json</c> catalogue. Applied on the next world reload.</summary>
  public string RecipeLevel { get; set; } = "normal";
  #endregion
}

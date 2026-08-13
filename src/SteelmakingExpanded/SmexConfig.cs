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
    // 0.9.7: the furnace and converter rebalance. Melting is now a rate metered by heat margin and
    // air rather than a gate, the blast furnace out-yields a bloomery, the converter remelts scrap,
    // and every figure downstream of those moved with them. A saved config carrying the old numbers
    // would leave a furnace that cannot reach its own melting point on cold blast, or blowers that
    // cannot feed the new air demand, so the whole rebalanced set is pushed out. The blast-mix keys
    // are also renamed to burden; a saved value under the old name is dropped and takes its default.
    new()
    {
      ToVersion = "0.9.7",
      ResetFields =
      [
        // Heat and melt model
        nameof(BfNaturalMaxTemp),
        nameof(BfMaxMoltenIron),
        nameof(BfMaxMoltenSlag),
        nameof(BfIronPerMeltCycle),
        nameof(BfSlagPerMeltCycle),
        nameof(BfIronTapDrainPerTick),
        nameof(BfSlagTapDrainPerTick),
        // Air, exhaust and stack
        nameof(TuyereIntakeVolume),
        nameof(AirBlowerOutputPerSecond),
        nameof(SmokestackGasIntakeVolume),
        // Converter
        nameof(BessemerConverterCapacity),
        nameof(BessemerBlastPerSecond),
        // The scrap heat penalty never reached the bath model; wiring it in makes these live
        // numbers for the first time, and the blast bonus they are set against now saturates.
        // The conversion-rate ramp moves with them so air and speed top out at the same pressure.
        nameof(BessemerColdScrapLossCoefficient),
        nameof(BessemerPressureTempGainMax),
        nameof(BessemerPressureTempGainHalf),
        nameof(BessemerPressureReference),
        // Molten flow
        nameof(MoltenFlowRate),
        nameof(MoltenMinFlowAmount),
        nameof(CanalDefaultUnitCapacity),
      ],
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
  public int MoltenFlowRate { get; set; } = 100;

  /// <summary>
  /// Minimum difference (units) between two canal cells before any metal moves between them that
  /// tick - the dribble a run does not bother with. Raising it costs a levelling connection up to
  /// its value in head, so keep it small: at ten it took twenty units of head per block and a run
  /// died three blocks from its source.
  /// </summary>
  public int MoltenMinFlowAmount { get; set; } = 1;

  /// <summary>
  /// Metal (units) one metal bit is worth. The exchange rate between the molten system's units and
  /// solid metal, used wherever the two meet: chiselling a frozen charge or canal, the nuggets a
  /// furnace leaves when it goes out, and the scrap a converter remelts. One key rather than one per
  /// site, so the recovery rate and the remelt rate cannot drift apart into a duplication loop.
  /// </summary>
  public int MoltenUnitsPerBit { get; set; } = 5;

  /// <summary>Default per-canal-block capacity (units) when a block sets no <c>maxUnits</c> attribute.</summary>
  public int CanalDefaultUnitCapacity { get; set; } = 100;

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

  #region Burden
  /// <summary>Burden units that must be loaded into the hearth before the furnace can fire.</summary>
  public int BurdenRequiredToFire { get; set; } = 320;

  /// <summary>
  /// Burden units a lit furnace must keep in its hearth to stay lit. A furnace robbed below this
  /// has too thin a column to hold a working bed and goes out; the burden left in the piles is not
  /// consumed or spoiled, so a relit furnace picks it back up once the hearth is refilled to
  /// <see cref="BurdenRequiredToFire"/>.
  /// </summary>
  public int BurdenRequiredToRun { get; set; } = 144;

  /// <summary>
  /// Seconds a lit burden pile keeps burning outside a blown furnace before it goes out. A pile
  /// no furnace is drawing air through has nothing feeding it, so it burns down and stops - the burden
  /// is left cold and unchanged, ready to be lit again. Long enough to light a whole hearth by hand
  /// before the first piles die.
  /// </summary>
  public int BurdenBurnTime { get; set; } = 300;
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

  /// <summary>
  /// Air (L/s) the blower injects per unit of engine power (Cornish 0.2/0.4/0.8 → 60/120/240 L/s,
  /// Watt 0.3 → 90 L/s); output pressure tracks the engine's inlet steam ×
  /// <see cref="PipesAndPowerExpanded.PpexValues.SteamEngineEfficiency"/>.
  /// <para>
  /// Sized so a steam blower feeds SEVERAL blast furnaces, or one driven hard - a Watt covers three
  /// at cold-blast baseline, and a Cornish on high covers one in full overdrive with room to spare.
  /// That spread is the whole point of building one: the mechanical blower it replaces manages
  /// exactly one furnace at baseline. The old 48 could not manage even that - 48 × 0.3 = 14.4 L/s
  /// against a 24 L/s draw - which is why a steam-blown plant ran slower than a water-driven one.
  /// </para>
  /// </summary>
  public float AirBlowerOutputPerSecond { get; set; } = 300f;
  #endregion

  #region Mechanical blower
  /// <summary>
  /// Air (L/s) the twin-tub blower approaches at unbounded axle speed - the volume its tubs can
  /// sweep. Never actually reached: output follows
  /// <c>MpBlowerMaxLitres * speed / (speed + MpBlowerHalfOutputSpeed)</c>, proportional near zero
  /// and flattening as the bellows run out of time to refill between strokes.
  /// <para>
  /// Sized off the WATERWHEEL, which is the drive this is meant to be built around. A well-fed
  /// wheel settles near speed 0.29 against this blower and delivers 20 L/s - one tuyere, so an
  /// ungeared wheel runs a blast furnace at reduced rate. One large gear (vanilla ratio 5.5) takes
  /// the same wheel to speed 1.6 and 60 L/s, which covers both tuyeres with headroom: gearing up is
  /// what the furnace is built around. The curve is what stops it there - 5.5x the speed buys 3x
  /// the air - and the ceiling means no gear train ever turns bellows into a steam blower.
  /// </para>
  /// </summary>
  public float MpBlowerMaxLitres { get; set; } = 110f;

  /// <summary>
  /// Axle speed at which the twin-tub blower delivers half of <see cref="MpBlowerMaxLitres"/>.
  /// Sets how quickly the curve flattens: lower makes the bellows saturate sooner and flattens the
  /// return on gearing up, higher keeps them closer to straight proportion.
  /// </summary>
  public float MpBlowerHalfOutputSpeed { get; set; } = 1.3f;

  /// <summary>
  /// Pressure ceiling (atm) the bellows can seal against, whatever drives them. Above
  /// <see cref="BfBlastPressureThreshold"/> so bellows run a blast furnace, below
  /// <see cref="BlastPressureThreshold"/> so they can never blow the converter - the gate between
  /// an iron shop and a steel one. Reaching it is a separate question from being allowed to: that
  /// is what <see cref="MpBlowerLoadPerAtm"/> decides.
  /// </summary>
  public float MpBlowerMaxPressure { get; set; } = 2.0f;

  /// <summary>Shaft load the bellows draw against an empty main - their own linkage friction.</summary>
  public float MpBlowerBaseLoad { get; set; } = 0.05f;

  /// <summary>
  /// Extra shaft load per atm of back-pressure in the blast main. A displacement blower's torque
  /// draw is set by the pressure it works against, which is what makes the drive decide the
  /// pressure actually reached: a weak drive is dragged slower as the main fills and settles at a
  /// lower pressure, a strong one holds full output up to <see cref="MpBlowerMaxPressure"/>. Keep
  /// the load at that ceiling under twice an engine's rated load or the generator reads it as an
  /// overload and cuts out, cycling the line instead of merely labouring.
  /// </summary>
  public float MpBlowerLoadPerAtm { get; set; } = 0.05f;
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

  /// <summary>
  /// Per-second rate the regenerator loses heat into the air it reheats into hot blast, at a draw of
  /// <see cref="CowperIntakeVolume"/>. Scaled by the air actually flowing, so a furnace in overdrive
  /// drains its stove several times faster than one on baseline - which is what makes alternating
  /// two stoves an operating decision rather than a formality.
  /// </summary>
  public float CowperCoolingSpeedAir { get; set; } = 0.0012f;

  /// <summary>
  /// Per-second rate a stove bleeds heat to its surroundings whatever else it is doing, toward
  /// the ambient air temperature where it stands. Brickwork this size holds its charge a long while - the
  /// default is a half-life of roughly twenty minutes - but a charged stove cannot be banked
  /// indefinitely and left as a permanent asset.
  /// </summary>
  public float CowperIdleCoolingSpeed { get; set; } = 0.0006f;

  /// <summary>Share of its temperature the spent exhaust keeps after the regenerator has taken its
  /// heat, on its way out to the smoke stack.</summary>
  [ExConfigRange(0, 1)]
  public float CowperSpentExhaustTempFraction { get; set; } = 0.4f;

  /// <summary>Gas (L/s) the cowper stove draws each tick from each of its intakes - the furnace exhaust it soaks heat from, and the air it reheats into hot blast. Also the reference draw its air-side heat loss is scaled against.</summary>
  public float CowperIntakeVolume { get; set; } = 24f;
  #endregion

  #region Blast furnace
  /// <summary>
  /// Hearth ceiling (°C) on cold blast - air straight off a blower. Deliberately set
  /// above <see cref="BfIronMeltingPoint"/>: a furnace blown with plain air melts iron on its own,
  /// just slowly and with little margin. Hot blast is an improvement, never a requirement.
  /// </summary>
  public float BfNaturalMaxTemp { get; set; } = 1540f;

  /// <summary>Hearth ceiling (°C) on blast at or above <see cref="BfBlastTempReference"/>.</summary>
  public float BfBoostedMaxTemp { get; set; } = 1740f;

  /// <summary>
  /// Blast temperature (°C) that buys the full boost. The ceiling and the heating rate both
  /// interpolate from their cold-blast figure to their hot-blast one across
  /// ambient..this, so a part-charged stove gives a part boost instead of
  /// nothing. Equal to <see cref="CowperMaxTemperature"/>, so a fully charged regenerator is exactly
  /// what it takes.
  /// </summary>
  public float BfBlastTempReference { get; set; } = 1240f;

  /// <summary>Hearth heating rate (°C/s) on cold blast.</summary>
  public float BfHeatRateBase { get; set; } = 4f;

  /// <summary>Hearth heating rate (°C/s) on blast at or above <see cref="BfBlastTempReference"/>.</summary>
  public float BfHeatRateHot { get; set; } = 8f;

  /// <summary>Hearth heating rate (°C/s) with no blast at all - the natural draught through the stack.</summary>
  public float BfHeatRateUnblown { get; set; } = 2f;

  /// <summary>Hearth temperature (°C) the moment the charge catches, before the blast works on it.</summary>
  public float BfIgnitionTemperature { get; set; } = 900f;

  /// <summary>Temperature (°C) the hearth must reach (and hold) to start melting iron.</summary>
  public float BfIronMeltingPoint { get; set; } = 1482f;

  /// <summary>Seconds below <see cref="BfIronMeltingPoint"/> before a melting furnace falls back to firing.</summary>
  public float BfMeltStallSeconds { get; set; } = 30f;

  /// <summary>Melt-rate multiplier at zero margin over <see cref="BfIronMeltingPoint"/>.</summary>
  public float BfMeltSpeedBase { get; set; } = 0.2f;

  /// <summary>Extra melt-rate multiplier per 100 °C of margin over <see cref="BfIronMeltingPoint"/>.</summary>
  public float BfMeltGainPer100C { get; set; } = 0.7f;

  /// <summary>Floor on the melt-rate multiplier once the hearth is over the melting point.</summary>
  public float BfMeltSpeedMin { get; set; } = 0.5f;

  /// <summary>Ceiling on the melt-rate multiplier, however hot the hearth runs.</summary>
  public float BfMeltSpeedMax { get; set; } = 2.0f;

  /// <summary>Maximum molten iron (units) the furnace can hold before stalling.</summary>
  public float BfMaxMoltenIron { get; set; } = 4800f;

  /// <summary>Maximum molten slag (units) the furnace can hold before stalling.</summary>
  public float BfMaxMoltenSlag { get; set; } = 1200f;

  /// <summary>Seconds a single disruption (a starved hearth, exhaust drawn back down the tuyeres) is
  /// tolerated before the furnace goes out. Two at once put it out immediately.</summary>
  public float BfDisruptionGraceSeconds { get; set; } = 30f;

  /// <summary>Seconds an open charging door is tolerated before the furnace goes out.</summary>
  public float BfDoorOpenGraceSeconds { get; set; } = 10f;

  /// <summary>
  /// Seconds a hearth being lit survives with no blast arriving before its piles go out. A furnace
  /// draws air from the moment its first pile catches, not from the moment the whole hearth is
  /// alight, so this is what stops a player lighting the charge at leisure and starting the blower
  /// afterwards. Short: the blower is meant to be running before the torch comes out.
  /// </summary>
  public float BfUnblownIgnitionSeconds { get; set; } = 10f;

  /// <summary>
  /// Share of a tuyere's rated volume a just-lit hearth draws, before it has heated. The demand
  /// climbs from here to the melt model's own floor as the hearth approaches iron's melting point,
  /// so a player watching the panel sees the requirement rise as the furnace comes up instead of a
  /// figure that sits still until it is already melting.
  /// </summary>
  [ExConfigRange(0, 1)]
  public float BfAirDemandIgnition { get; set; } = 0.25f;

  /// <summary>Molten iron (units) drained per tick through an open lower tap.</summary>
  public int BfIronTapDrainPerTick { get; set; } = 40;

  /// <summary>Molten slag (units) drained per tick through an open upper tap.</summary>
  public int BfSlagTapDrainPerTick { get; set; } = 40;

  /// <summary>
  /// Pressure ceiling the furnace pushes its exhaust to. This is what the flue can build against a
  /// sealed main, so it also sets how much headroom the run has before it saturates and the furnace
  /// stalls. At the old implicit 1 atm the pressure-relief valve in the setup diagrams could never
  /// open, since its own gate defaults to 1 atm. Well under the 5 atm iron / 10 atm steel burst.
  /// A run with any open end is clamped back to 1 atm regardless.
  /// </summary>
  public float BfExhaustOutputPressure { get; set; } = 2.0f;

  /// <summary>
  /// Exhaust (L/s) the furnace vents in total per litre of blast the tuyeres drew, shared across
  /// its gas outlets. What is blown in has to come back out, so a furnace driven hard makes
  /// proportionally more flue gas - which is what charges the regenerators faster and closes the
  /// hot-blast loop. Keep <see cref="SmokestackGasIntakeVolume"/> at or above what this yields at
  /// the furnace's maximum draw, or one stack cannot clear one furnace.
  /// </summary>
  public float BfExhaustPerAirDrawn { get; set; } = 1.0f;

  /// <summary>Exhaust (L/s) the furnace vents in total on natural draught alone, with no blast at
  /// all, shared across its gas outlets.</summary>
  public float BfExhaustBaseVolume { get; set; } = 24f;

  /// <summary>Share of the hearth temperature the flue gas carries out of the furnace.</summary>
  [ExConfigRange(0, 1)]
  public float BfExhaustTempFraction { get; set; } = 0.8f;

  /// <summary>Seconds between melt cycles while melting.</summary>
  public float BfMeltIntervalSec { get; set; } = 10f;

  /// <summary>Molten iron (units) produced per melt cycle.</summary>
  public float BfIronPerMeltCycle { get; set; } = 102f;

  /// <summary>Molten slag (units) produced per melt cycle.</summary>
  public float BfSlagPerMeltCycle { get; set; } = 17f;

  /// <summary>Burden consumed per melt cycle.</summary>
  public int BfBurdenPerMeltCycle { get; set; } = 16;

  /// <summary>
  /// Air/blast (L/s) one tuyere draws at a melt rate of 1.0. The furnace scales this by whatever
  /// rate its heat margin supports, so a cold-blast furnace breathes about 24 L/s across both
  /// tuyeres and one in overdrive about 80 - and the blast it actually gets meters the melt in turn.
  /// </summary>
  public float TuyereIntakeVolume { get; set; } = 20f;

  /// <summary>
  /// Pressure (atm) the air at a tuyere must reach before it counts as blast and the furnace will
  /// fire. Separate from the converter's <see cref="BlastPressureThreshold"/>: iron is gated low
  /// enough for a mechanical blower to reach, steel is not.
  /// </summary>
  public float BfBlastPressureThreshold { get; set; } = 1.5f;
  #endregion

  #region Bessemer converter
  /// <summary>Molten-metal capacity (units) of the converter vessel.</summary>
  public int BessemerConverterCapacity { get; set; } = 2400;

  /// <summary>Blast (L/s) the converter draws from its gas intake at a conversion rate of 1.0.
  /// Scaled by the rate the blast pressure supports, like the furnace's tuyere draw.</summary>
  public float BessemerBlastPerSecond { get; set; } = 24.0f;

  /// <summary>Seconds of blast a charge needs to refine iron into steel at a conversion rate of 1.0.</summary>
  public float BessemerProcessDuration { get; set; } = 300f;

  /// <summary>
  /// Bath temperature (°C) the blow itself produces, before losses. Burning the carbon out of the
  /// iron is what heats a converter - it has no fuel of its own - so this is the ceiling the process
  /// works down from rather than a temperature anything holds it at.
  /// </summary>
  public float BessemerBaseTemperature { get; set; } = 1850f;

  /// <summary>Temperature (°C) the bath sheds to its surroundings continuously.</summary>
  public float BessemerRadiationLoss { get; set; } = 50f;

  /// <summary>
  /// Temperature (°C) the bath must hold for the blow to refine. Below it the process stalls, and if
  /// the bath keeps falling it freezes into the existing solidified/chisel path.
  /// </summary>
  public float BessemerRefineTemperature { get; set; } = 1500f;

  /// <summary>
  /// Ceiling (°C) on the bath heat blast pressure can buy, approached but never reached - see
  /// <see cref="BessemerPressureTempGainHalf"/> for the curve. Harder blast burns the carbon faster
  /// and hotter, which is what pays for a cold scrap charge, so a plant that wants to remelt a lot
  /// of scrap builds pressure for it rather than being handed a fixed allowance.
  /// </summary>
  public float BessemerPressureTempGainMax { get; set; } = 580f;

  /// <summary>
  /// Blast pressure (atm) over <see cref="BlastPressureThreshold"/> at which half of
  /// <see cref="BessemerPressureTempGainMax"/> is realised. The gain saturates rather than climbing
  /// per atm without end: the first atmosphere over the gate is worth far more than the fifth, so
  /// the scrap allowance tops out at a pressure a real plant can hold instead of one no line
  /// survives. Same saturating shape the twin-tub blower's output uses.
  /// </summary>
  public float BessemerPressureTempGainHalf { get; set; } = 0.7f;

  /// <summary>
  /// Bath temperature (°C) lost per unit of cold scrap charged. There is deliberately no scrap cap:
  /// scrap is pure cold mass on the heat balance, and the point where it drags the bath under
  /// <see cref="BessemerRefineTemperature"/> is the limit. Set against the blast curve so the
  /// allowance runs from about 16% of capacity on the minimum blast to 40% at 6 atm - the ceiling a
  /// Cornish engine driving an air blower can hold, its 8 atm break pressure through the engine's
  /// 0.75 efficiency - and never reaches half the vessel at any pressure. A full stack of 128 bits
  /// is 640 units, about 27% of capacity, and needs roughly 3 atm behind it.
  /// </summary>
  public float BessemerColdScrapLossCoefficient { get; set; } = 0.8f;

  /// <summary>
  /// Comma-separated item codes the converter accepts as cold scrap. The converter is the remelter -
  /// the blast furnace smelts ore and takes no scrap at all, because a route that turns iron back
  /// into furnace feed is a duplication loop. Each item is worth
  /// <see cref="MoltenUnitsPerBit"/> units.
  /// </summary>
  public string BessemerScrapCodes { get; set; } =
    "game:metalbit-iron,game:metalbit-steel";

  /// <summary>Minimum conversion-rate multiplier, at or below <see cref="BlastPressureThreshold"/>.</summary>
  public float BessemerSpeedMin { get; set; } = 0.5f;

  /// <summary>Maximum conversion-rate multiplier, however hard the vessel is blown.</summary>
  public float BessemerSpeedMax { get; set; } = 2.0f;

  /// <summary>
  /// Blast pressure (atm) over <see cref="BlastPressureThreshold"/> that buys the full conversion
  /// rate. At the default the gate pressure gives the minimum and 6 atm the maximum - the same
  /// ceiling <see cref="BessemerPressureTempGainMax"/> is set against, so air drawn, blow speed and
  /// scrap tolerance all stop rewarding at the pressure a plant can actually hold. A shorter
  /// reference would leave pressure above it buying scrap tolerance at no cost in air.
  /// </summary>
  public float BessemerPressureReference { get; set; } = 3.5f;

  /// <summary>Minimum geared mechanical speed for the converter to count as powered.</summary>
  public float BessemerPowerSpeedThreshold { get; set; } = 0.1f;

  /// <summary>Shaft load the converter's transmission puts on its axle network - the whole of the
  /// vessel's drive cost. Keep it under what the intended drive can hold at
  /// <see cref="BessemerPowerSpeedThreshold"/>, or the vessel will not turn.</summary>
  public float BessemerTransmissionResistance { get; set; } = 0.25f;

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

  #region Hopper bell (burden maker)
  /// <summary>Items the hopper magazine can buffer.</summary>
  public int HopperMaxMagazineCapacity { get; set; } = 48;

  /// <summary>Crushed iron ore consumed per burden batch.</summary>
  public int HopperIronOreRequired { get; set; } = 12;

  /// <summary>
  /// Iron nuggets consumed per burden batch when the hopper is fed raw ore instead of crushed.
  /// The two mix freely within one batch at that exchange rate. Equal to
  /// <see cref="HopperIronOreRequired"/> by default, so a nugget is worth exactly what a piece of
  /// crushed ore is: the furnace takes its iron however it comes, and crushing is a convenience on
  /// this route rather than a toll. Raise it to make the pulverizer pay again.
  /// </summary>
  public int HopperNuggetRequired { get; set; } = 12;

  /// <summary>
  /// Extra ore units a roasted piece of iron ore carries over an unroasted one. Roasting is a
  /// separate heat step that returns the ore one for one, so the burden pays a premium for it
  /// rather than promoting it to a tier of its own. Only mods that roast ore produce a feed this
  /// applies to; at 0 roasted ore is worth exactly what raw ore is.
  /// <para>
  /// Measured against the bloomery route the ore could take instead - a vanilla iron nugget smelts
  /// at 20 to the ingot, so 5 units of metal - the shipped rates put raw ore at 1.70x that and
  /// roasted ore at 1.98x.
  /// </para>
  /// </summary>
  public int HopperRoastedOreBonus { get; set; } = 2;

  /// <summary>Coke consumed per burden batch. Whole coke: the furnace no longer takes the
  /// crushed intermediate, and 3 crushed coke was 1.5 coke at the old 1:2 crush ratio.</summary>
  public int HopperCokeRequired { get; set; } = 2;

  /// <summary>
  /// Charcoal consumed per burden batch when the hopper is fed charcoal instead of coke.
  /// Charcoal carries roughly half the carbon of coke, so it takes twice as many pieces; the two
  /// fuels mix freely within one batch at that exchange rate.
  /// </summary>
  public int HopperCharcoalRequired { get; set; } = 4;

  /// <summary>Lime consumed per burden batch.</summary>
  public int HopperLimeRequired { get; set; } = 1;

  /// <summary>Burden produced per batch.</summary>
  public int HopperBurdenProduced { get; set; } = 16;

  /// <summary>Burden dropped per output pulse.</summary>
  public int HopperDropAmount { get; set; } = 4;
  #endregion

  #region Smoke stack
  /// <summary>
  /// Exhaust gas (L/s) the smoke stack vents from the network. Sized to clear a furnace driven flat
  /// out: a stack that cannot keep up chokes the flue, which stalls the furnace it is meant to serve.
  /// </summary>
  public float SmokestackGasIntakeVolume { get; set; } = 96.0f;
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

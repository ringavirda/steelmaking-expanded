using System.Linq;
using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockStructures.Converter;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The Bessemer converter's charge-handling process, exercised directly (the full
/// <c>OnProductionTick</c> is gated on four aligned peripherals + a constructed vessel, but the per-
/// state steps are reachable on their own): filling from the input canal cell, pouring into the
/// output cell, refining iron into steel, and latching solid below the melting point. Plus the
/// player-facing operability gate, which must refuse an unbuilt converter.
/// </summary>
public class ConverterControlProcessTests {
  private const string Iron = "game:ingot-iron";
  private const string Steel = "game:ingot-steel";
  private const string IronBits = "game:metalbit-iron";
  private const string SteelBits = "game:metalbit-steel";

  /// <summary>Total cold scrap in the vessel, across every kind charged.</summary>
  private static int ScrapUnits(BlockEntityConverterControl be) =>
    (int)ReflectionHelpers.GetProperty(be, "_scrapUnits")!;

  // Structure-local peripheral offsets (mirrors the private constants in the control).
  private static readonly (int x, int y, int z) InputTapLocal = (1, 1, 2);
  private static readonly (int x, int y, int z) OutputStartLocal = (1, -2, 2);

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem(Steel, 1500f);
    world.RegisterItem(IronBits);
    world.RegisterItem(SteelBits);
    return world;
  }

  private static BlockEntityConverterControl Control(TestWorld world) {
    var be = new BlockEntityConverterControl {
      Pos = new BlockPos(0, 8, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:converterbessemercontrol-north",
        1,
        ("side", "north")
      ),
    };
    world.Attach(be);
    // Establish the structure angle so the peripheral offsets resolve (Initialize would do this).
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation");
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  /// <summary>Places a molten-canal cell at the control's resolved peripheral offset and returns it.</summary>
  private static BlockEntityMoltenCanal PlaceCell(
    TestWorld world,
    BlockEntityConverterControl control,
    (int x, int y, int z) local
  ) {
    var pos = (BlockPos)
      ReflectionHelpers.Invoke(
        control,
        "GetGlobalPos",
        local.x,
        local.y,
        local.z
      )!;
    var cell = new BlockEntityMoltenCanal {
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanal-straight-ns",
        9,
        ("type", "straight"),
        ("orientation", "ns")
      ),
    };
    world.Place(pos, cell.Block, cell);
    world.Attach(cell);
    return cell;
  }

  #region Filling

  [Fact]
  public void TickFilling_draws_molten_metal_from_the_input_cell() {
    var world = NewWorld();
    var be = Control(world);
    var input = PlaceCell(world, be, InputTapLocal);
    input.PushMetal(50, Metal(world, Iron, 1400f), world.World);

    ReflectionHelpers.Invoke(be, "TickFilling", 1f);

    Assert.Equal(50, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
    Assert.True(input.IsCellEmpty); // drained into the vessel
  }

  [Fact]
  public void TickFilling_does_nothing_when_the_input_cell_is_empty() {
    var world = NewWorld();
    var be = Control(world);
    PlaceCell(world, be, InputTapLocal); // present but empty

    ReflectionHelpers.Invoke(be, "TickFilling", 1f);

    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
  }

  #endregion

  #region Pouring

  [Fact]
  public void TickPouring_pushes_the_charge_into_the_output_cell() {
    var world = NewWorld();
    var be = Control(world);
    var output = PlaceCell(world, be, OutputStartLocal);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, 1400f));
    ReflectionHelpers.SetField(be, "_contentUnits", 40);

    ReflectionHelpers.Invoke(be, "TickPouring", 1f);

    Assert.True(output.CellAmount > 0);
    Assert.Equal(Iron, output.CellMetalType);
    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
  }

  #endregion

  #region Refining

  [Fact]
  public void CompleteRefining_turns_the_iron_charge_into_steel() {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, 1400f));
    ReflectionHelpers.SetField(be, "_contentUnits", 50);

    ReflectionHelpers.Invoke(be, "CompleteRefining");

    var content = (ItemStack)ReflectionHelpers.GetField(be, "_content")!;
    Assert.Equal(Steel, content.Collectible.Code.ToString());
  }

  /// <summary>
  /// Cold scrap has not melted into anything yet, so breaking the vessel has to give each kind back
  /// as itself - iron bits as iron, steel bits as steel - rather than one lump of whatever the molten
  /// charge happened to be. Also covers a vessel holding nothing but scrap, which is the intended
  /// charging order and used to drop nothing at all.
  /// </summary>
  [Fact]
  public void Unmelted_scrap_comes_back_as_the_kinds_that_went_in() {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.Invoke(be, "AddScrap", IronBits, 200); // 40 bits
    ReflectionHelpers.Invoke(be, "AddScrap", SteelBits, 100); // 20 bits

    be.OnConverterBroken();

    Assert.Equal(2, world.Drops.Count);
    var iron = world.Drops.Single(d =>
      d.Collectible.Code.ToString() == IronBits
    );
    var steel = world.Drops.Single(d =>
      d.Collectible.Code.ToString() == SteelBits
    );
    Assert.Equal(200 / SmexValues.MoltenUnitsPerBit, iron.StackSize);
    Assert.Equal(100 / SmexValues.MoltenUnitsPerBit, steel.StackSize);
    Assert.Equal(0, ScrapUnits(be));
  }

  /// <summary>Which kinds are in the vessel has to survive a reload, or the drop is a guess.</summary>
  [Fact]
  public void The_scrap_ledger_round_trips_through_the_tree() {
    var world = NewWorld();
    var src = Control(world);
    ReflectionHelpers.Invoke(src, "AddScrap", IronBits, 200);
    ReflectionHelpers.Invoke(src, "AddScrap", SteelBits, 100);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Control(world);
    dst.FromTreeAttributes(tree, world.World);
    dst.OnConverterBroken();

    Assert.Equal(
      200 / SmexValues.MoltenUnitsPerBit,
      world
        .Drops.Single(d => d.Collectible.Code.ToString() == IronBits)
        .StackSize
    );
    Assert.Equal(
      100 / SmexValues.MoltenUnitsPerBit,
      world
        .Drops.Single(d => d.Collectible.Code.ToString() == SteelBits)
        .StackSize
    );
  }

  /// <summary>
  /// Scrap comes back as steel unit for unit. Material loss on a remelt belongs to the ironworks
  /// mod's design, not to this one.
  /// </summary>
  [Fact]
  public void Charged_scrap_melts_into_the_heat_without_loss() {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, 1400f));
    ReflectionHelpers.SetField(be, "_contentUnits", 50);
    ReflectionHelpers.Invoke(be, "AddScrap", IronBits, 200);

    ReflectionHelpers.Invoke(be, "CompleteRefining");

    Assert.Equal(250, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
    Assert.Equal(0, ScrapUnits(be));
  }

  /// <summary>
  /// Air drawn and blow speed have to stop rewarding at the same pressure the bath heat does, or
  /// pressure past that point buys scrap tolerance for nothing. Both ceilings are 6 atm - what a
  /// Cornish engine driving an air blower can hold.
  /// </summary>
  [Fact]
  public void The_blow_reaches_full_rate_at_the_same_pressure_the_bath_heat_does() {
    var world = NewWorld();
    var be = Control(world);

    float Speed(float atm) =>
      (float)ReflectionHelpers.Invoke(be, "ConversionSpeed", atm)!;

    Assert.Equal(SmexValues.BessemerSpeedMin, Speed(2.5f), 3);
    Assert.Equal(SmexValues.BessemerSpeedMax, Speed(6.0f), 3);
    // Still climbing through the band, so every atmosphere up to the ceiling costs more air.
    Assert.True(
      Speed(4.0f) > Speed(3.0f) && Speed(4.0f) < SmexValues.BessemerSpeedMax,
      "the rate must still be climbing at 4 atm, not already capped"
    );
  }

  /// <summary>
  /// Cold scrap is mass on the bath's heat balance, and that is the whole limit on how much can be
  /// charged - there is no cap by design, only the point where the bath falls under the refining
  /// temperature. The coefficient existed and was displayed to the player but never reached
  /// <c>ProcessTemperature</c>, so scrap was free and the readout's figure was fiction.
  /// </summary>
  [Fact]
  public void Charged_scrap_costs_the_bath_its_documented_heat() {
    var world = NewWorld();
    var be = Control(world);
    float pressure = SmexValues.BlastPressureThreshold;

    float clean = (float)
      ReflectionHelpers.Invoke(be, "ProcessTemperature", pressure)!;
    ReflectionHelpers.Invoke(be, "AddScrap", IronBits, 100);
    float withScrap = (float)
      ReflectionHelpers.Invoke(be, "ProcessTemperature", pressure)!;

    Assert.Equal(
      100 * SmexValues.BessemerColdScrapLossCoefficient,
      clean - withScrap,
      2
    );
  }

  /// <summary>
  /// Refines at <paramref name="atm"/> with the vessel <paramref name="fraction"/> full of cold
  /// scrap?
  /// </summary>
  private static bool ScrapRefines(TestWorld world, float fraction, float atm) {
    var be = Control(world);
    ReflectionHelpers.Invoke(
      be,
      "AddScrap",
      IronBits,
      (int)(fraction * SmexValues.BessemerConverterCapacity)
    );
    return (float)ReflectionHelpers.Invoke(be, "ProcessTemperature", atm)!
      > SmexValues.BessemerRefineTemperature;
  }

  /// <summary>
  /// The two operating points the scrap coefficient and the blast curve are set against: about 15%
  /// of the vessel on the minimum blast, rising to 40% at 6 atm - the ceiling a Cornish engine
  /// driving an air blower can hold (8 atm break pressure through the engine's 0.75 efficiency).
  /// Between them sits the charge players actually tip in: a full stack of 128 bits is 640 units,
  /// about 27% of capacity.
  /// </summary>
  [Theory]
  [InlineData(0.15f, 2.5f, true)] // minimum blast carries 15%
  [InlineData(0.20f, 2.5f, false)] // and no more
  [InlineData(0.27f, 3.0f, true)] // a full stack of bits, on a modest blast
  [InlineData(0.40f, 6.0f, true)] // the best blast a plant can hold carries 40%
  [InlineData(0.40f, 5.0f, false)] // and it takes that much - 5 atm is short
  [InlineData(0.40f, 3.0f, false)]
  public void The_scrap_allowance_runs_from_15_percent_to_40_with_blast(
    float fraction,
    float atm,
    bool refines
  ) {
    Assert.Equal(refines, ScrapRefines(NewWorld(), fraction, atm));
  }

  /// <summary>
  /// The blast bonus saturates, so the allowance cannot be pushed arbitrarily by pressure alone.
  /// Past the plant's own ceiling it barely moves, and half a vessel of scrap is unreachable at any
  /// pressure at all. A linear gain per atm would clear both of these.
  /// </summary>
  [Fact]
  public void Pressure_past_the_useful_band_buys_almost_no_further_allowance() {
    var world = NewWorld();

    Assert.True(
      ScrapRefines(world, 0.42f, 10f),
      "10 atm - past anything a plant can hold - should still only reach ~43%"
    );
    Assert.False(ScrapRefines(world, 0.46f, 10f));
    Assert.False(
      ScrapRefines(world, 0.50f, 100f),
      "no pressure should reach half a vessel - the gain saturates"
    );
  }

  /// <summary>
  /// The trade the scrap term exists to create: a charge that stalls the blow at the minimum blast
  /// carries it once the pressure is raised. Without the term both cases read identically.
  /// </summary>
  [Fact]
  public void Blast_pressure_buys_back_the_heat_a_scrap_charge_costs() {
    var world = NewWorld();
    var be = Control(world);
    // Well past the 15% the minimum blast carries, inside the 40% a hard blast does.
    ReflectionHelpers.Invoke(be, "AddScrap", IronBits, 800);

    float atGate = (float)
      ReflectionHelpers.Invoke(
        be,
        "ProcessTemperature",
        SmexValues.BlastPressureThreshold
      )!;
    float blownHard = (float)
      ReflectionHelpers.Invoke(
        be,
        "ProcessTemperature",
        SmexValues.BlastPressureThreshold + 3f
      )!;

    Assert.True(
      atGate < SmexValues.BessemerRefineTemperature,
      $"1400 units of scrap should stall the blow at the gate, bath was {atGate} C"
    );
    Assert.True(
      blownHard > SmexValues.BessemerRefineTemperature,
      $"a harder blast should carry the same charge, bath was {blownHard} C"
    );
  }

  #endregion

  #region Solidify latch

  [Theory]
  [InlineData(1600f, false)] // above the 1500 melt point -> stays liquid
  [InlineData(300f, true)] // below melt -> latches solid
  public void UpdateSolidified_latches_against_the_melting_point(
    float temp,
    bool expected
  ) {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, temp));
    ReflectionHelpers.SetField(be, "_contentUnits", 30);

    ReflectionHelpers.Invoke(be, "UpdateSolidified");

    Assert.Equal(
      expected,
      (bool)ReflectionHelpers.GetField(be, "_solidified")!
    );
  }

  #endregion

  #region Operability gate

  [Fact]
  public void CanOperate_refuses_an_incomplete_structure_with_a_reason() {
    var world = NewWorld();
    var be = Control(world);

    bool ok = be.CanOperate(out string error);

    Assert.False(ok);
    Assert.NotEqual("", error);
  }

  [Fact]
  public void IsConverterPresent_is_false_with_no_vessel_placed() {
    var world = NewWorld();
    Assert.False(Control(world).IsConverterPresent());
  }

  #endregion
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using PipesAndPowerExpanded;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The twin-tub mechanical blower: throughput against axle speed, the shaft load that carries the
/// back-pressure, the pressure ceiling that keeps it to iron, the air budget it has to cover, and
/// the footprint its fillers reserve. The blast main and the mechanical network are too much to
/// fake wholesale, so the balance is driven through the public
/// <see cref="BlockEntityMpBlower.OutputAt"/> / <see cref="BlockEntityMpBlower.ShaftLoadAt"/> /
/// <see cref="BlockEntityMpBlower.ProduceAir"/> entry points and the footprint is resolved from
/// the shipped block JSON.
/// </summary>
public class MpBlowerTests {
  private const string Def =
    "src/SteelmakingExpanded/assets/smex/blocktypes/blastfurnace/mpblower.json";

  /// <summary>A blast furnace draws through two tuyeres; the blower has to feed both.</summary>
  private const int FurnaceTuyeres = 2;

  private static readonly BlockPos Origin = new(0, 16, 0);

  // Shaft speed measured in game on a waterwheel fed by five source blocks, taken from the blower's
  // own printed output at the time (40 L/s against the straight proportion of 140 L/s per unit
  // speed it ran on then).
  private const float UngearedWheelSpeed = 0.29f;

  // BEBehaviorMPLargeGear3m.ratio, read out of the shipped VSSurvivalMod assembly.
  private const float LargeGearRatio = 5.5f;

  private const float GearedWheelSpeed = UngearedWheelSpeed * LargeGearRatio;

  #region Speed response

  // A saturating curve, not a straight proportion: proportional near zero, then flattening as the
  // tubs run out of time to refill between strokes.
  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(0.25f, 17.742f)]
  [InlineData(0.5f, 30.556f)]
  [InlineData(1.0f, 47.826f)]
  [InlineData(1.3f, 55f)] // the half-output speed, by definition half the ceiling
  [InlineData(4f, 83.019f)]
  public void Output_follows_a_saturating_curve(float speed, float expected) {
    Assert.Equal(expected, BlockEntityMpBlower.OutputAt(speed), 3);
  }

  [Fact]
  public void The_swept_ceiling_is_approached_but_never_reached() {
    float ceiling = SmexValues.MpBlowerMaxLitres;

    Assert.True(BlockEntityMpBlower.OutputAt(1000f) < ceiling);
    Assert.True(BlockEntityMpBlower.OutputAt(1000f) > ceiling * 0.99f);
  }

  [Fact]
  public void Gearing_up_buys_less_air_than_it_buys_speed() {
    // The point of the curve. One large gear turns the blower 5.5x faster; under a straight
    // proportion that bought 5.5x the air and one wheel ran four furnaces.
    float speedGain = GearedWheelSpeed / UngearedWheelSpeed;
    float airGain =
      BlockEntityMpBlower.OutputAt(GearedWheelSpeed)
      / BlockEntityMpBlower.OutputAt(UngearedWheelSpeed);

    Assert.True(
      airGain < speedGain,
      $"gearing bought {airGain}x the air for {speedGain}x the speed"
    );
    Assert.Equal(3f, airGain, 1);
  }

  [Fact]
  public void A_barely_turning_shaft_still_blows_something() {
    // The band this replaced delivered NOTHING below speed 0.5 and full output only at 1.5, which
    // is the MP generator's unloaded top speed - unreachable once the blower's own load is on the
    // shaft. A Watt engine settled at 0.51 and read as 1% of rated. Bellows worked slowly should
    // blow slowly, not stop.
    Assert.True(BlockEntityMpBlower.OutputAt(0.05f) > 0f);
    Assert.True(BlockEntityMpBlower.OutputAt(0.51f) > 0f);
  }

  [Fact]
  public void A_stopped_or_reversed_shaft_blows_nothing() {
    Assert.Equal(0f, BlockEntityMpBlower.OutputAt(0f), 3);
    Assert.Equal(0f, BlockEntityMpBlower.OutputAt(-1f), 3);
  }

  [Fact]
  public void Retuning_the_ceiling_scales_the_whole_curve() {
    float ceiling = SmexValues.MpBlowerMaxLitres;
    float half = SmexValues.MpBlowerHalfOutputSpeed;
    try {
      SmexValues.Edit(c => {
        c.MpBlowerMaxLitres = 12f;
        c.MpBlowerHalfOutputSpeed = 1f;
      });

      Assert.Equal(6f, BlockEntityMpBlower.OutputAt(1f), 3);
      Assert.Equal(4f, BlockEntityMpBlower.OutputAt(0.5f), 3);
    } finally {
      SmexValues.Edit(c => {
        c.MpBlowerMaxLitres = ceiling;
        c.MpBlowerHalfOutputSpeed = half;
      });
    }
  }

  #endregion

  #region Shaft load

  [Fact]
  public void The_shaft_load_rises_with_the_pressure_the_bellows_work_against() {
    // This is where drive size decides the outcome. The network settles at
    // speed = drive budget / load, so a load that climbs with the main's pressure drags a weak
    // drive slower as the main fills - its output falls away and the pressure it holds settles low
    // - while a strong drive holds speed to the ceiling. Nothing here caps the pressure itself.
    float empty = BlockEntityMpBlower.ShaftLoadAt(0f);
    float half = BlockEntityMpBlower.ShaftLoadAt(1f);
    float full = BlockEntityMpBlower.ShaftLoadAt(
      SmexValues.MpBlowerMaxPressure
    );

    Assert.Equal(SmexValues.MpBlowerBaseLoad, empty, 4);
    Assert.True(half > empty);
    Assert.True(full > half);
    Assert.Equal(
      SmexValues.MpBlowerBaseLoad
        + SmexValues.MpBlowerLoadPerAtm * SmexValues.MpBlowerMaxPressure,
      full,
      4
    );
  }

  [Fact]
  public void A_negative_pressure_reading_never_credits_the_shaft() {
    Assert.Equal(
      SmexValues.MpBlowerBaseLoad,
      BlockEntityMpBlower.ShaftLoadAt(-5f),
      4
    );
  }

  [Fact]
  public void The_load_at_the_pressure_ceiling_stays_under_a_watt_stall_point() {
    // The MP generator cuts out entirely above twice its rated load, stopping the whole line rather
    // than merely labouring it. The smallest engine meant to drive a blower has to stay the right
    // side of that at full main pressure, or the shop cycles on and off.
    float wattRated =
      PpexValues.WattEngineMaxPower * PpexValues.MpLoadPerEnginePower;
    float atCeiling = BlockEntityMpBlower.ShaftLoadAt(
      SmexValues.MpBlowerMaxPressure
    );

    Assert.True(
      atCeiling < 2f * wattRated,
      $"the blower draws {atCeiling} at its pressure ceiling but a Watt engine "
        + $"stalls above {2f * wattRated}"
    );
  }

  #endregion

  #region Tier gate

  [Fact]
  public void The_blower_ceiling_sits_between_the_furnace_gate_and_the_converter_gate() {
    // This ordering IS the machine: a mechanically blown shop clears the blast furnace's air gate
    // and can never clear the Bessemer converter's, so bellows make iron and only a steam blower
    // makes steel. Any retune that lets the ceiling cross either gate hands out the wrong tier.
    Assert.True(
      SmexValues.BfBlastPressureThreshold < SmexValues.MpBlowerMaxPressure,
      $"the blower ceiling ({SmexValues.MpBlowerMaxPressure} atm) must clear the "
        + $"furnace gate ({SmexValues.BfBlastPressureThreshold} atm)"
    );
    Assert.True(
      SmexValues.MpBlowerMaxPressure < SmexValues.BlastPressureThreshold,
      $"the blower ceiling ({SmexValues.MpBlowerMaxPressure} atm) must stay under the "
        + $"converter gate ({SmexValues.BlastPressureThreshold} atm)"
    );

    Assert.Equal(1.5f, SmexValues.BfBlastPressureThreshold, 3);
    Assert.Equal(2.0f, SmexValues.MpBlowerMaxPressure, 3);
    Assert.Equal(2.5f, SmexValues.BlastPressureThreshold, 3);
  }

  #endregion

  #region Air budget

  /// <summary>
  /// What one blast furnace draws through both tuyeres at the COLD-BLAST baseline. A melting hearth
  /// wants every tuyere's rated volume, and more again as its heat margin drives the melt harder; a
  /// mechanically blown plant has no regenerators, so it sits at BfNaturalMaxTemp, whose margin is
  /// worth less than one rated volume - the baseline is the rated draw flat. Anything hotter is a
  /// steam blower's job, which is the progression the two machines exist to express.
  /// </summary>
  private static float BaselineFurnaceDraw() {
    float coldBlastMargin =
      SmexValues.BfNaturalMaxTemp - SmexValues.BfIronMeltingPoint;
    float baselineMeltSpeed = Math.Clamp(
      SmexValues.BfMeltSpeedBase
        + coldBlastMargin / 100f * SmexValues.BfMeltGainPer100C,
      SmexValues.BfMeltSpeedMin,
      SmexValues.BfMeltSpeedMax
    );
    return FurnaceTuyeres
      * SmexValues.TuyereIntakeVolume
      * Math.Max(1f, baselineMeltSpeed);
  }

  /// <summary>The most air one furnace can ask for - both tuyeres at the melt-rate ceiling.</summary>
  private static float OverdriveFurnaceDraw() =>
    FurnaceTuyeres * SmexValues.TuyereIntakeVolume * SmexValues.BfMeltSpeedMax;

  [Fact]
  public void A_geared_waterwheel_covers_both_tuyeres_of_one_blast_furnace() {
    // The design anchor: the geared wheel is the setup the furnace is built around, and it is what
    // has to cover the furnace. Gearing up is a real build step, not an optimisation.
    float delivered = BlockEntityMpBlower.OutputAt(GearedWheelSpeed);

    Assert.True(
      BaselineFurnaceDraw() <= delivered,
      $"a geared waterwheel delivers {delivered} L/s, but two tuyeres draw "
        + $"{BaselineFurnaceDraw()} L/s at the cold-blast baseline"
    );
  }

  [Fact]
  public void An_ungeared_waterwheel_runs_a_furnace_slowly_rather_than_not_at_all() {
    // The other half of the ruling. An ungeared wheel falls short of the baseline draw, so the
    // furnace melts in proportion to the air arriving - it works, just slowly. It must never fall
    // to nothing: bellows worked slowly blow slowly.
    float delivered = BlockEntityMpBlower.OutputAt(UngearedWheelSpeed);

    Assert.True(delivered > 0f, "an ungeared wheel must still blow something");
    Assert.True(
      delivered < BaselineFurnaceDraw(),
      $"an ungeared waterwheel delivers {delivered} L/s, which should fall short "
        + $"of the {BaselineFurnaceDraw()} L/s baseline draw - otherwise gearing up buys nothing"
    );
  }

  [Fact]
  public void A_waterwheel_cannot_hold_a_furnace_in_overdrive_even_geared() {
    // Driving a hearth to its boosted ceiling takes air no waterwheel can make, geared or not -
    // that is what a steam blower is for. The ceiling on the curve is what guarantees it: no gear
    // train can get past MpBlowerMaxLitres.
    float overdriveDraw = OverdriveFurnaceDraw();

    Assert.True(
      BlockEntityMpBlower.OutputAt(GearedWheelSpeed) < overdriveDraw,
      $"a geared waterwheel delivers {BlockEntityMpBlower.OutputAt(GearedWheelSpeed)} L/s, "
        + $"which should fall short of the {overdriveDraw} L/s an overdriven furnace draws"
    );
  }

  [Fact]
  public void No_gear_train_makes_the_bellows_a_steam_blower() {
    // What the ceiling is really for. Gearing harder always pays something, so a big enough train
    // does eventually overdrive a furnace - that is a build cost, not a hole. What it can never do
    // is catch the steam blower, which is the machine the progression is built on.
    Assert.True(
      SmexValues.MpBlowerMaxLitres < SmexValues.AirBlowerOutputPerSecond,
      $"the bellows' swept ceiling ({SmexValues.MpBlowerMaxLitres} L/s) must stay under the "
        + $"steam blower's {SmexValues.AirBlowerOutputPerSecond} L/s, whatever drives it"
    );
  }

  [Fact]
  public void A_steam_engine_turns_the_blower_faster_than_a_waterwheel() {
    // The intended progression: a waterwheel runs it slowly, an engine runs it hard. The two drives
    // are different kinds of source - the wheel seeks a speed, the generator holds a power budget -
    // so this is not a given, and it is the whole reason the machine is worth putting on a shaft.
    float load = BlockEntityMpBlower.ShaftLoadAt(
      SmexValues.BfBlastPressureThreshold
    );
    float wheel = WaterwheelSpeed(flowRate: 1f, load: load);
    float engine = EngineSpeed(PpexValues.WattEngineMaxPower, load);

    Assert.True(
      engine > wheel * 2f,
      $"a Watt engine settles at {engine} but a waterwheel already reaches {wheel}"
    );
  }

  /// <summary>
  /// Where a vanilla waterwheel of <paramref name="flowRate"/> settles under
  /// <paramref name="load"/>. <c>BEBehaviorMPRotor.GetTorque</c> returns
  /// <c>(capableSpeed - speed) * TorqueFactor</c> and the network balances at torque == resistance,
  /// with <c>TargetSpeed = min(0.3, flowRate)</c> and <c>TorqueFactor = flowRate</c>.
  /// </summary>
  private static float WaterwheelSpeed(float flowRate, float load) =>
    Math.Max(0f, Math.Min(0.3f, flowRate) - load / flowRate);

  /// <summary>
  /// Where an engine's MP generator settles under <paramref name="load"/>. It is a constant-power
  /// source, so <c>speed = budget / load</c>, and its torque tapers to zero at the engine's own
  /// <c>ShaftSpeed</c> - a shaft coupled straight to the engine cannot turn faster than the engine
  /// strokes, so the cap rises with the engine's power rather than being a constant.
  /// </summary>
  private static float EngineSpeed(float enginePower, float load) =>
    Math.Min(
      // BlockEntityEngine.ShaftSpeed: the beam's clip-speed multiplier (0.5 + power) converted into
      // network speed units by pi/5, since the animator runs a 60-frame cycle at 30x that multiplier
      // while the network turns the axle at 5x speed radians a second.
      (0.5f + enginePower)
        * MathF.PI
        / 5f,
      enginePower
        * PpexValues.MpLoadPerEnginePower
        * PpexValues.MpRatedSpeed
        / load
    );

  [Fact]
  public void One_smoke_stack_clears_one_furnace_driven_flat_out() {
    // What is blown in comes back out, so the stack has to be sized against the furnace's HARDEST
    // draw, not its baseline. The furnace shares its exhaust across its outlets rather than venting
    // the whole draw through each, which is what made a correctly sized stack look undersized.
    float peakExhaust =
      OverdriveFurnaceDraw() * SmexValues.BfExhaustPerAirDrawn;

    Assert.True(
      SmexValues.SmokestackGasIntakeVolume >= peakExhaust,
      $"a smoke stack vents {SmexValues.SmokestackGasIntakeVolume} L/s but a furnace in overdrive "
        + $"makes {peakExhaust} L/s of flue gas - a stack that cannot keep up chokes the furnace"
    );
    // And on natural draught alone, which is the other end of the range.
    Assert.True(
      SmexValues.SmokestackGasIntakeVolume >= SmexValues.BfExhaustBaseVolume,
      "a stack must clear an unblown furnace's natural draught too"
    );
  }

  [Fact]
  public void A_watt_engine_driving_the_blower_is_never_starved() {
    // The reported failure, as a number, driven off the shipped constants end to end. The engine is
    // a constant-power source, so the shaft settles at speed = budget / load; under the old band
    // that speed landed at 1% of rated output and the furnace starved.
    // Measured at the furnace's own blast gate, which is the binding operating point: below it
    // the furnace refuses the blast outright, and above it the main is fuller than the furnace
    // can draw down - so this is the hardest pressure the blower must hold while actually
    // feeding one.
    // What it does NOT claim is full coverage. Bellows are sized off the waterwheel, and a small
    // engine bolted straight to them clears well over one tuyere but not both - gear the shaft, or
    // build the steam air blower, which is the point of having two machines.
    float speed = EngineSpeed(
      PpexValues.WattEngineMaxPower,
      BlockEntityMpBlower.ShaftLoadAt(SmexValues.BfBlastPressureThreshold)
    );
    float delivered = BlockEntityMpBlower.OutputAt(speed);

    Assert.True(
      delivered > SmexValues.TuyereIntakeVolume,
      $"a Watt engine settles the shaft at {speed} and delivers {delivered} L/s, "
        + $"which should comfortably clear one tuyere's {SmexValues.TuyereIntakeVolume} L/s"
    );
    Assert.True(
      delivered > BlockEntityMpBlower.OutputAt(UngearedWheelSpeed),
      "an engine must beat a bare waterwheel on the same machine"
    );
  }

  [Theory]
  [InlineData(0f, 1f)] // shaft stopped
  [InlineData(1.5f, 0f)] // no time elapsed
  public void ProduceAir_pushes_nothing_without_speed_or_time(
    float speed,
    float dt
  ) {
    Assert.Equal(0f, new BlockEntityMpBlower().ProduceAir(speed, dt), 3);
  }

  [Fact]
  public void ProduceAir_pushes_nothing_with_no_blast_main_attached() {
    var world = new TestWorld();
    var be = new BlockEntityMpBlower { Pos = Origin };
    world.Place(Origin, ShippedBlower(), be);
    world.Attach(be);

    Assert.Equal(0f, be.ProduceAir(1.5f, 1f), 3);
  }

  #endregion

  #region Footprint

  // Resolved at the block's own StructureAngle, never at a bare 0: the footprint is authored along
  // local +z but raised AWAY from the player, so a north blower reserves world -z. Asserting the
  // local frame instead is what let the volume spawn on top of the player unnoticed.
  [Fact]
  public void The_fillers_reserve_the_two_high_three_deep_volume() {
    var block = ShippedBlower();
    var cells = StructureFillers.FootprintCells(
      block,
      Origin,
      block.StructureAngle
    );

    Assert.Equal(
      new[] { (0, 1, 0), (0, 0, -1), (0, 1, -1), (0, 0, -2), (0, 1, -2) },
      cells
        .Select(c =>
          (c.Pos.X - Origin.X, c.Pos.Y - Origin.Y, c.Pos.Z - Origin.Z)
        )
        .ToArray()
    );
  }

  [Fact]
  public void The_port_cell_hosts_the_mechanical_power_port_on_its_west_face() {
    var block = ShippedBlower();
    var cells = StructureFillers.FootprintCells(
      block,
      Origin,
      block.StructureAngle
    );
    FillerCell port = Assert.Single(cells, c => c.Behaviors != null);

    Assert.Equal(Origin.UpCopy(), port.Pos);
    FillerBehavior hosted = Assert.Single(port.Behaviors!);
    Assert.Equal("exlib.BEBehaviorMPFillerPort", hosted.Code);
    // Authored east in the def; the structure rotation turns it west for a north blower.
    Assert.Equal(BlockFacing.WEST, hosted.ConnectorFace);
  }

  [Fact]
  public void The_block_reads_its_port_and_outlet_cells_from_the_shipped_def() {
    // The two named cells must land inside the reserved volume: the axle couples to the port cell
    // and the blast main butts against the outlet cell, so a def edit that moves either off the
    // footprint leaves the blower unable to be driven or unable to deliver.
    var block = ShippedBlower();
    var footprint = StructureFillers
      .FootprintCells(block, Origin, block.StructureAngle)
      .Select(c => c.Pos)
      .ToList();

    Assert.Equal(Origin.AddCopy(0, 1, 0), block.MpPortWorldPos(Origin));
    Assert.Equal(Origin.AddCopy(0, 0, -2), block.BlastOutletWorldPos(Origin));
    Assert.Contains(block.MpPortWorldPos(Origin), footprint);
    Assert.Contains(block.BlastOutletWorldPos(Origin), footprint);
  }

  [Fact]
  public void Every_element_the_cycle_moves_is_keyframed_at_both_ends() {
    // An element keyframed at frame 0 but not at the last frame holds its mid-stroke value through
    // the tail and then snaps home once per revolution. Note that the wrap itself is NOT a no-op:
    // it is a rendered one-frame segment carrying the last 360/quantityframes degrees of the turn,
    // which is why the shaft ends the clip one frame short of a whole turn rather than back at its
    // starting value. LoopingAnimationTests holds that half of the rule for every shipped shape.
    JToken cycle = Assert.Single(
      Shape()["animations"]!,
      a => (string?)a["name"] == "cycle"
    );
    int last = (int)cycle["quantityframes"]! - 1;

    var keys = cycle["keyframes"]!.ToList();
    var atFirst = Elements(keys.Single(k => (int)k["frame"]! == 0));
    var atLast = Elements(keys.Single(k => (int)k["frame"]! == last));

    Assert.Equal(atFirst.OrderBy(e => e), atLast.OrderBy(e => e));
  }

  private static IEnumerable<string> Elements(JToken keyframe) =>
    ((JObject)keyframe["elements"]!).Properties().Select(p => p.Name);

  private static JObject Shape() =>
    JObject.Parse(
      File.ReadAllText(
        Path.Combine(
          RepoRoot(),
          "src/SteelmakingExpanded/assets/smex/shapes/blastfurnace/mpblower.json".Replace(
            '/',
            Path.DirectorySeparatorChar
          )
        )
      )
    );

  [Fact]
  public void Both_port_cells_accept_an_attached_block() {
    // A port cell exists to be attached to, so its filler must opt into allowAttach. Without it the
    // filler swallows the click and the axle or main can only be sneak-placed onto the port - which
    // reads as the connector not working at all.
    var block = ShippedBlower();
    var cells = StructureFillers.FootprintCells(
      block,
      Origin,
      block.StructureAngle
    );

    foreach (
      BlockPos port in new[]
      {
        block.MpPortWorldPos(Origin),
        block.BlastOutletWorldPos(Origin),
      }
    )
      Assert.True(
        cells.Single(c => c.Pos.Equals(port)).AllowAttach,
        $"the filler at {port} carries a port, so it must allow attachment"
      );
  }

  [Fact]
  public void The_blast_main_leaves_the_far_end_of_the_housing() {
    // A north blower delivers to the north: the housing runs away from the player.
    Assert.Equal(BlockFacing.NORTH, ShippedBlower().OutletFace);
  }

  #endregion

  #region Asset loading

  /// <summary>
  /// A north-facing blower primed with the shipped block JSON's <c>attributes</c>, so the offset and
  /// filler accessors read the same node the game hands them (the headless harness has no asset
  /// pipeline, so the def is loaded by hand).
  /// </summary>
  private static BlockMpBlower ShippedBlower() {
    var block = TestBlocks.Configure(
      new BlockMpBlower(),
      "smex:mpblower-north",
      400,
      ("side", "north")
    );
    block.Attributes = new JsonObject(Attributes(Def));
    return block;
  }

  private static JToken Attributes(string repoRelativePath) {
    string full = Path.Combine(
      RepoRoot(),
      repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
    );
    Assert.True(File.Exists(full), $"missing asset: {full}");
    JObject def = JObject.Parse(File.ReadAllText(full));
    JToken? attributes = def["attributes"];
    Assert.True(attributes != null, $"{repoRelativePath} has no attributes");
    return attributes!;
  }

  private static string RepoRoot() {
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (
      dir != null
      && !File.Exists(Path.Combine(dir.FullName, "VintageStory.sln"))
    )
      dir = dir.Parent;
    Assert.True(dir != null, "could not locate repo root (VintageStory.sln)");
    return dir!.FullName;
  }

  #endregion
}

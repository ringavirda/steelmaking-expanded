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

  #region Speed response

  // Straight proportion: the tubs sweep a fixed volume per turn, so the rate follows the shaft.
  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(0.25f, 35f)]
  [InlineData(0.5f, 70f)]
  [InlineData(1.0f, 140f)]
  [InlineData(1.5f, 210f)]
  [InlineData(4f, 560f)]
  public void Output_is_a_straight_proportion_of_axle_speed(
    float speed,
    float expected
  ) {
    Assert.Equal(expected, BlockEntityMpBlower.OutputAt(speed), 3);
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
  public void Retuning_the_displacement_scales_the_whole_curve() {
    float rate = SmexValues.MpBlowerLitresPerSpeed;
    try {
      SmexValues.Edit(c => c.MpBlowerLitresPerSpeed = 12f);

      Assert.Equal(12f, BlockEntityMpBlower.OutputAt(1f), 3);
      Assert.Equal(6f, BlockEntityMpBlower.OutputAt(0.5f), 3);
    } finally {
      SmexValues.Edit(c => c.MpBlowerLitresPerSpeed = rate);
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

  [Fact]
  public void A_waterwheel_covers_both_tuyeres_of_one_blast_furnace() {
    // The design anchor: the blower is meant to be built around a waterwheel, turning slowly. A
    // vanilla wheel is a speed-seeking source - torque = (TargetSpeed - speed) * flowRate - so it
    // settles at TargetSpeed - load/flowRate, and TargetSpeed is min(0.3, flowRate). Even unloaded
    // it cannot pass 0.3, which is why the old band's 0.5 floor made a waterwheel useless: it was
    // above the wheel's top speed outright.
    float speed = WaterwheelSpeed(
      flowRate: 1f,
      load: BlockEntityMpBlower.ShaftLoadAt(SmexValues.BfBlastPressureThreshold)
    );
    float delivered = BlockEntityMpBlower.OutputAt(speed);

    // The furnace's draw scales with the melt rate its heat margin supports, so the figure to cover
    // is the COLD-BLAST baseline: a mechanically blown plant has no regenerators, so its hearth sits
    // at BfNaturalMaxTemp and melts at the rate that margin buys. Anything hotter than that is a
    // steam blower's job, which is the progression the two machines exist to express.
    float coldBlastMargin =
      SmexValues.BfNaturalMaxTemp - SmexValues.BfIronMeltingPoint;
    float baselineMeltSpeed = Math.Clamp(
      SmexValues.BfMeltSpeedBase
        + coldBlastMargin / 100f * SmexValues.BfMeltGainPer100C,
      SmexValues.BfMeltSpeedMin,
      SmexValues.BfMeltSpeedMax
    );
    float draw =
      FurnaceTuyeres * SmexValues.TuyereIntakeVolume * baselineMeltSpeed;

    Assert.True(
      speed > 0f,
      "a waterwheel must be able to turn the blower at all"
    );
    Assert.True(
      draw <= delivered,
      $"a waterwheel settles the shaft at {speed} and delivers {delivered} L/s, "
        + $"but two tuyeres draw {draw} L/s at the cold-blast baseline"
    );
  }

  [Fact]
  public void A_waterwheel_cannot_hold_a_furnace_in_overdrive() {
    // The other half of the same ruling: one mechanical blower covers one furnace at baseline and
    // no more. Driving a hearth to its boosted ceiling takes air a waterwheel cannot make, so
    // overdrive is what a steam blower is for.
    float speed = WaterwheelSpeed(
      flowRate: 1f,
      load: BlockEntityMpBlower.ShaftLoadAt(SmexValues.BfBlastPressureThreshold)
    );
    float delivered = BlockEntityMpBlower.OutputAt(speed);
    float overdriveDraw =
      FurnaceTuyeres
      * SmexValues.TuyereIntakeVolume
      * SmexValues.BfMeltSpeedMax;

    Assert.True(
      delivered < overdriveDraw,
      $"a waterwheel delivers {delivered} L/s, which should fall short of the "
        + $"{overdriveDraw} L/s an overdriven furnace draws"
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
  public void A_watt_engine_driving_the_blower_covers_both_tuyeres() {
    // The reported failure, as a number, driven off the shipped constants end to end. The engine is
    // a constant-power source, so the shaft settles at speed = budget / load; under the old band
    // that speed landed at 1% of rated output and the furnace starved.
    // Measured at the furnace's own blast gate, which is the binding operating point: below it
    // the furnace refuses the blast outright, and above it the main is fuller than the furnace
    // can draw down - so this is the hardest pressure the blower must hold while actually
    // feeding one.
    float speed = EngineSpeed(
      PpexValues.WattEngineMaxPower,
      BlockEntityMpBlower.ShaftLoadAt(SmexValues.BfBlastPressureThreshold)
    );
    float delivered = BlockEntityMpBlower.OutputAt(speed);
    float draw = FurnaceTuyeres * SmexValues.TuyereIntakeVolume;

    Assert.True(
      draw <= delivered,
      $"a Watt engine settles the shaft at {speed} and delivers {delivered} L/s, "
        + $"but two tuyeres draw {draw} L/s"
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

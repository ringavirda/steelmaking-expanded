using System;
using System.IO;
using System.Linq;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The twin-tub mechanical blower: the axle-speed response curve, the pressure band that keeps it to
/// iron, the air budget it has to cover, and the footprint its fillers reserve. The blast main and
/// the mechanical network are too much to fake wholesale, so the balance is driven through the
/// public <see cref="BlockEntityMpBlower.SpeedFraction"/> / <see cref="BlockEntityMpBlower.ProduceAir"/>
/// entry points and the footprint is resolved from the shipped block JSON.
/// </summary>
public class MpBlowerTests {
  private const string Def =
    "src/SteelmakingExpanded/assets/smex/blocktypes/blastfurnace/mpblower.json";

  /// <summary>A blast furnace draws through two tuyeres; the blower has to feed both.</summary>
  private const int FurnaceTuyeres = 2;

  private static readonly BlockPos Origin = new(0, 16, 0);

  #region Speed response

  // The shipped band: nothing below 0.5, full output at 1.5, straight line between.
  [Theory]
  [InlineData(0f, 0f)]
  [InlineData(0.25f, 0f)]
  [InlineData(0.5f, 0f)]
  [InlineData(0.75f, 0.25f)]
  [InlineData(1.0f, 0.5f)]
  [InlineData(1.25f, 0.75f)]
  [InlineData(1.5f, 1f)]
  [InlineData(4f, 1f)]
  public void SpeedFraction_ramps_linearly_across_the_speed_band(
    float speed,
    float expected
  ) {
    Assert.Equal(expected, BlockEntityMpBlower.SpeedFraction(speed), 3);
  }

  [Fact]
  public void The_band_edges_are_the_configured_speeds_not_hard_coded_ones() {
    // "At or below" the floor and "at or above" the ceiling - the boundaries are inclusive, so an
    // axle sitting exactly on the floor delivers nothing rather than a sliver.
    Assert.Equal(
      0f,
      BlockEntityMpBlower.SpeedFraction(SmexValues.MpBlowerMinSpeed),
      3
    );
    Assert.Equal(
      1f,
      BlockEntityMpBlower.SpeedFraction(SmexValues.MpBlowerMaxSpeed),
      3
    );
    Assert.Equal(0f, BlockEntityMpBlower.SpeedFraction(-1f), 3);
  }

  [Fact]
  public void Retuning_the_speed_band_moves_the_response_with_it() {
    float min = SmexValues.MpBlowerMinSpeed;
    float max = SmexValues.MpBlowerMaxSpeed;
    try {
      SmexValues.Edit(c => {
        c.MpBlowerMinSpeed = 2f;
        c.MpBlowerMaxSpeed = 4f;
      });

      // Speeds that ran the shipped band flat out now sit under the new floor.
      Assert.Equal(0f, BlockEntityMpBlower.SpeedFraction(1.5f), 3);
      Assert.Equal(0f, BlockEntityMpBlower.SpeedFraction(2f), 3);
      Assert.Equal(0.5f, BlockEntityMpBlower.SpeedFraction(3f), 3);
      Assert.Equal(1f, BlockEntityMpBlower.SpeedFraction(4f), 3);
    } finally {
      SmexValues.Edit(c => {
        c.MpBlowerMinSpeed = min;
        c.MpBlowerMaxSpeed = max;
      });
    }
  }

  [Fact]
  public void A_collapsed_speed_band_is_all_or_nothing_rather_than_a_divide_by_zero() {
    float min = SmexValues.MpBlowerMinSpeed;
    float max = SmexValues.MpBlowerMaxSpeed;
    try {
      SmexValues.Edit(c => {
        c.MpBlowerMinSpeed = 1f;
        c.MpBlowerMaxSpeed = 1f;
      });

      Assert.Equal(0f, BlockEntityMpBlower.SpeedFraction(1f), 3);
      Assert.Equal(1f, BlockEntityMpBlower.SpeedFraction(1.01f), 3);
    } finally {
      SmexValues.Edit(c => {
        c.MpBlowerMinSpeed = min;
        c.MpBlowerMaxSpeed = max;
      });
    }
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
  public void Rated_output_covers_both_tuyeres_of_one_blast_furnace() {
    float draw = FurnaceTuyeres * SmexValues.TuyereIntakeVolume;
    Assert.True(
      draw <= SmexValues.MpBlowerOutputPerSecond,
      $"two tuyeres draw {draw} L/s but the blower is rated at "
        + $"{SmexValues.MpBlowerOutputPerSecond} L/s"
    );
  }

  [Fact]
  public void Half_rated_output_still_covers_both_tuyeres() {
    // A vanilla waterwheel turns at roughly speed 1, the midpoint of the band, so half rated
    // output - that is the case a player actually hits, not the rated figure.
    float fraction = BlockEntityMpBlower.SpeedFraction(1.0f);
    Assert.Equal(0.5f, fraction, 3);

    float delivered = SmexValues.MpBlowerOutputPerSecond * fraction;
    float draw = FurnaceTuyeres * SmexValues.TuyereIntakeVolume;
    Assert.True(
      draw <= delivered,
      $"a waterwheel delivers {delivered} L/s but two tuyeres draw {draw} L/s"
    );
  }

  [Theory]
  [InlineData(0.25f, 1f)] // axle under the floor
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

    Assert.Equal(0f, be.ProduceAir(SmexValues.MpBlowerMaxSpeed, 1f), 3);
  }

  #endregion

  #region Footprint

  [Fact]
  public void The_fillers_reserve_the_two_high_three_deep_volume() {
    var cells = StructureFillers.FootprintCells(ShippedBlower(), Origin, 0);

    Assert.Equal(
      new[] { (0, 1, 0), (0, 0, 1), (0, 1, 1), (0, 0, 2), (0, 1, 2) },
      cells
        .Select(c =>
          (c.Pos.X - Origin.X, c.Pos.Y - Origin.Y, c.Pos.Z - Origin.Z)
        )
        .ToArray()
    );
  }

  [Fact]
  public void The_port_cell_hosts_the_mechanical_power_port_on_its_east_face() {
    var cells = StructureFillers.FootprintCells(ShippedBlower(), Origin, 0);
    FillerCell port = Assert.Single(cells, c => c.Behaviors != null);

    Assert.Equal(Origin.UpCopy(), port.Pos);
    FillerBehavior hosted = Assert.Single(port.Behaviors!);
    Assert.Equal("exlib.BEBehaviorMPFillerPort", hosted.Code);
    Assert.Equal(BlockFacing.EAST, hosted.ConnectorFace);
  }

  [Fact]
  public void The_block_reads_its_port_and_outlet_cells_from_the_shipped_def() {
    // The two named cells must land inside the reserved volume: the axle couples to the port cell
    // and the blast main butts against the outlet cell, so a def edit that moves either off the
    // footprint leaves the blower unable to be driven or unable to deliver.
    var block = ShippedBlower();
    var footprint = StructureFillers
      .FootprintCells(block, Origin, 0)
      .Select(c => c.Pos)
      .ToList();

    Assert.Equal(Origin.AddCopy(0, 1, 0), block.MpPortWorldPos(Origin));
    Assert.Equal(Origin.AddCopy(0, 0, 2), block.BlastOutletWorldPos(Origin));
    Assert.Contains(block.MpPortWorldPos(Origin), footprint);
    Assert.Contains(block.BlastOutletWorldPos(Origin), footprint);
  }

  [Fact]
  public void The_blast_main_leaves_the_far_end_of_the_housing() {
    Assert.Equal(BlockFacing.SOUTH, ShippedBlower().OutletFace);
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

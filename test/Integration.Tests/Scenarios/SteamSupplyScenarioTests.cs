using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Whole-process scenarios for the steam plant's support systems (handbook starter setup): a
/// pressure-relief valve gating an over-pressured boiler main so a Watt engine runs in band, and a
/// hand-cranked manual pump lifting pond water into a boiler line before any engine exists. Like the
/// other plant scenarios these lay the real machines + pipe lines into one <see cref="Scene"/> and
/// advance them together.
/// </summary>
public class SteamSupplyScenarioTests {
  #region Pressure-valve regulation (boiler main → relief valve → engine in band)

  [Fact]
  public void A_relief_valve_bleeds_an_over_pressured_main_so_the_engine_runs_in_band() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var plant = new RegulatedEnginePlant(
      scene,
      new BlockPos(0, 8, 0),
      gateAtm: 2.5f
    );
    scene.Build();

    // The boiler holds the main at 5 atm; the relief valve bleeds the excess above the 2.5 atm gate
    // into the drain each tick, so the gated line never runs away from the engine.
    plant.RunCharged(5f, 4);

    Assert.True(
      plant.DrainVolume > 0f,
      "the valve should have bled overflow into the drain"
    );
    Assert.True(
      plant.MainPressure < 5f,
      "the relieved main should sit below the boiler's charge"
    );
    Assert.False(plant.Engine.IsBroken, "the engine should not have burst");
    Assert.True(
      plant.Engine.IsRunning,
      "the engine should be driven by the gated main"
    );
  }

  [Fact]
  public void With_the_gate_above_the_charge_the_valve_never_opens() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    // Gate raised to the steel rating: 5 atm of charge is below it, so nothing is relieved.
    var plant = new RegulatedEnginePlant(
      scene,
      new BlockPos(0, 8, 0),
      gateAtm: 10f
    );
    scene.Build();

    plant.RunCharged(5f, 4);

    Assert.Equal(0f, plant.DrainVolume, 2); // valve stayed shut
    Assert.True(plant.MainPressure > 4f, "the unrelieved main stays high");
  }

  #endregion

  #region Manual pump (engine-free water start)

  [Fact]
  public void Cranking_the_manual_pump_lifts_pond_water_into_the_output_main() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var plant = new ManualPumpPlant(scene, new BlockPos(0, 8, 0));
    scene.Build();

    plant.FillPond(30f); // standing water on the input line
    plant.Crank(3); // player holds right-click for three ticks

    Assert.True(plant.OutputIsWater, "the output main should carry water");
    Assert.True(
      plant.OutputVolume > 0f,
      "cranking should lift water into the output main"
    );
  }

  [Fact]
  public void An_uncranked_pump_moves_no_water() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var plant = new ManualPumpPlant(scene, new BlockPos(0, 8, 0));
    scene.Build();

    plant.FillPond(30f);
    scene.Step(3); // never cranked

    Assert.Equal(0f, plant.OutputVolume, 3);
  }

  #endregion

  #region Condenser (closed water loop's recovery leg)

  [Fact]
  public void Spent_steam_is_condensed_and_recovered_into_the_water_line() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var plant = new CondenserPlant(scene, new BlockPos(0, 8, 0));
    scene.Build();

    plant.ChargeSteam(300f).ChargeFeedWater(40f);
    float steamBefore = plant.SteamVolume;
    scene.Step(4);

    Assert.True(
      plant.Condensing,
      "the condenser should report condensing with steam on the line"
    );
    Assert.True(
      plant.SteamVolume < steamBefore,
      "spent steam should be drawn off the line"
    );
    Assert.True(
      plant.RecoveredIsWater,
      "the recovered line should carry water"
    );
    Assert.True(
      plant.RecoveredVolume > 0f,
      "recovered water should reach the line back to the boiler"
    );
  }

  [Fact]
  public void Without_steam_the_condenser_condenses_nothing() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var plant = new CondenserPlant(scene, new BlockPos(0, 8, 0));
    scene.Build();

    plant.ChargeFeedWater(40f); // feed water but no steam to condense
    scene.Step(4);

    Assert.False(plant.Condensing);
  }

  #endregion
}

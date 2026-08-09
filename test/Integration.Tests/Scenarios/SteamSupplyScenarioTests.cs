using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine;
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
  #region Condensate outlet

  /// <summary>
  /// A running engine's condensate has to reach a line plumbed onto its water-outlet face. Nothing
  /// else in the fixtures wires that face, so this is the only cover on the outlet half of the water
  /// loop - the half a player closes back into the boiler.
  /// </summary>
  [Fact]
  public void A_running_engine_sends_its_condensate_into_a_plumbed_outlet() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var enginePos = new BlockPos(0, 8, 0);
    var plant = new RegulatedEnginePlant(scene, enginePos, gateAtm: 2.5f);

    // A sealed one-cell condensate line on the engine's water-outlet face.
    var outletFace = plant.EngineBlock.WaterOutletFace;
    BlockPos outlet = enginePos.AddCopy(outletFace);
    EnginePlant.Pipe(scene, outlet, EnginePlant.Axis(outletFace), 90);
    scene.Block(outlet.AddCopy(outletFace), PpexScenes.Cap(91));
    scene.Build();

    plant.RunCharged(3f, 4);

    Assert.True(plant.Engine.IsRunning, "the engine should be driven");
    Assert.True(
      scene.NetworkAt<PipeNetwork>(outlet)!.State?.Volume > 0f,
      "the condensate should have gone into the connected line"
    );
  }

  /// <summary>
  /// A closed water loop holds its main brim-full, which is the normal state of a working plant, not
  /// a fault: the outlet must back up quietly there. Only a face with nothing plumbed onto it, or one
  /// plumbed into a run carrying gas, sprays where the player can see it.
  /// </summary>
  [Fact]
  public void Only_an_outlet_with_nowhere_to_send_water_sprays() {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var line = new BlockPos(0, 8, 0);
    EnginePlant.Pipe(scene, line, "we", 92);
    scene.Block(line.WestCopy(), PpexScenes.Cap(93));
    scene.Block(line.EastCopy(), PpexScenes.Cap(94));
    scene.Build();

    var net = scene.NetworkAt<PipeNetwork>(line)!;

    Assert.True(
      BlockEntityEngine.OutletSpills(null),
      "an unplumbed outlet should spray"
    );

    // Brim-full of water: the line refuses more, but a closed loop always reads this way.
    net.TryProduceLiquid(
      PpexValues.LitresPerPipe * 4f,
      90f,
      0f,
      scene.World.Accessor
    );
    Assert.False(
      net.TryProduceLiquid(1f, 90f, 0f, scene.World.Accessor),
      "the premise: a brim-full line takes no more water"
    );
    Assert.False(
      BlockEntityEngine.OutletSpills(net),
      "a backed-up water line should not spray"
    );

    // The same line carrying gas can never take water - that is a plumbing mistake worth showing.
    net.TryConsumeLiquid(net.State!.Volume, scene.World.Accessor);
    net.TryProduceGas(60f, 150f, "Steam", scene.World.Accessor);
    Assert.True(
      BlockEntityEngine.OutletSpills(net),
      "an outlet plumbed into a gas run should spray"
    );
  }

  #endregion

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

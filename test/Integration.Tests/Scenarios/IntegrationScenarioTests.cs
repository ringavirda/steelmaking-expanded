using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.MathTools;
using Xunit;
using BoilerState = PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntityBoiler.BoilerState;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Whole-setup integration scenarios built from <see cref="Scene"/> + <see cref="SceneDiagram"/>: lay
/// out a network (or a machine feeding one), advance the simulation with a single step, and assert on
/// the emergent cross-cell / cross-machine state. These exercise the wiring between components, not a
/// single unit in isolation.
/// </summary>
public class IntegrationScenarioTests {
  [Fact]
  public void Sealed_run_pressurises_and_broadcasts_to_every_pipe() {
    // A five-cell west-east steam main, capped at both ends.
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    PpexScenes.PipeLegend(scene).Layer("#=====#");
    scene.Build();

    // Inject steam at one end and let the network settle.
    var head = new BlockPos(1, 0, 0);
    var tail = new BlockPos(5, 0, 0);
    scene
      .NetworkAt<PipeNetwork>(head)!
      .TryProduceGas(
        450f,
        150f,
        "Steam",
        scene.World.Accessor,
        maxOutputPressure: 10f
      );
    scene.Step();

    // One network spans the whole run, and the far pipe sees the same pressure via broadcast.
    Assert.Same(
      scene.NetworkAt<PipeNetwork>(head),
      scene.NetworkAt<PipeNetwork>(tail)
    );
    var farPipe = scene.EntityAt<BlockEntityPipe>(tail)!;
    Assert.Equal("Steam", farPipe.Medium);
    Assert.True(farPipe.Pressure > 0f, "pressure should reach the far end");
  }

  [Fact]
  public void Two_separate_runs_stay_isolated() {
    // Two capped mains with a gap between them - two independent networks.
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    PpexScenes.PipeLegend(scene).Layer("#==#   #==#");
    scene.Build();

    var left = new BlockPos(1, 0, 0);
    var right = new BlockPos(8, 0, 0);
    Assert.NotSame(
      scene.NetworkAt<PipeNetwork>(left),
      scene.NetworkAt<PipeNetwork>(right)
    );

    scene
      .NetworkAt<PipeNetwork>(left)!
      .TryProduceGas(
        300f,
        150f,
        "Steam",
        scene.World.Accessor,
        maxOutputPressure: 10f
      );
    scene.Step();

    // The right-hand main never saw the steam.
    Assert.True(scene.EntityAt<BlockEntityPipe>(left)!.Pressure > 0f);
    Assert.Null(scene.NetworkAt<PipeNetwork>(right)!.State);
  }

  [Fact]
  public void A_boiling_boiler_charges_an_attached_steam_main() {
    // Boiler + a sealed two-cell vertical steam riser on its outlet: the boiler should bleed steam
    // into the pipe network until their pressures equalise (the connected-vessel PushSteam path).
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var boiler = new BoilerFixture(scene, new BlockPos(0, 8, 0));

    BlockPos attach = boiler.SteamPipeAttachPos; // pipe cell directly above the outlet connector
    var riser = PpexScenes.PipeLegend(scene);
    // Seal the riser bottom (outlet connector cell) and top so it can hold pressure.
    riser
      .Layer("#", y: attach.Y - 1, originX: attach.X, originZ: attach.Z) // bottom cap
      .Layer("I", y: attach.Y, originX: attach.X, originZ: attach.Z) // riser cell 1
      .Layer("I", y: attach.Y + 1, originX: attach.X, originZ: attach.Z) // riser cell 2
      .Layer("#", y: attach.Y + 2, originX: attach.X, originZ: attach.Z); // top cap
    scene.Build();

    // Heating (not Boiling) still pushes steam out the outlet but doesn't generate more, so the
    // boiler's steam strictly drops as it charges the main - isolating the PushSteam transfer.
    boiler.Prime(BoilerState.Heating, water: 200f, steam: 400f);
    scene.Step(3);

    var steamNet = scene.NetworkAt<PipeNetwork>(attach)!;
    Assert.NotNull(steamNet.State);
    Assert.True(
      steamNet.State!.Volume > 0f,
      "the boiler should have charged the steam main"
    );
    Assert.True(
      boiler.SteamVolume < 400f,
      "the boiler should have given up steam to the main"
    );
  }
}

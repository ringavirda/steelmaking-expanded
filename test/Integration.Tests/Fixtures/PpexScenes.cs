using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Boiler.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using BoilerState = PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntityBoiler.BoilerState;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// ppex-specific building blocks for <see cref="Scene"/> integration tests: a pipe-diagram legend and
/// a boiler fixture. These hold the mod knowledge (which glyph is which oriented pipe, how a boiler is
/// stood up) so the scenario tests read as layouts + assertions.
/// </summary>
internal static class PpexScenes {
  /// <summary>A cap block that seals a pipe end so a run can pressurise instead of leaking.</summary>
  public static Block Cap(int id = 99) =>
    TestBlocks.Configure(new Block(), "game:rock", id);

  /// <summary>One shared oriented pipe block (the engine reuses a single instance across a run).</summary>
  public static BlockPipe Pipe(string orientation, int id) =>
    PipeTestWorld.MakePipe(orientation: orientation, id: id);

  /// <summary>
  /// Registers a pipe-network diagram legend on <paramref name="scene"/>:
  /// <c>=</c> west-east pipe, <c>|</c> north-south pipe, <c>I</c> vertical (up-down) pipe,
  /// <c>#</c> a sealing cap. Each glyph shares one oriented block instance, as the game does.
  /// </summary>
  public static SceneDiagram PipeLegend(Scene scene) {
    var we = Pipe("we", 1);
    var ns = Pipe("ns", 2);
    var ud = Pipe("ud", 3);
    var cap = Cap();

    BlockEntityPipe Be(BlockPos p, BlockPipe block) =>
      new() { Pos = p.Copy(), Block = block };

    return new SceneDiagram()
      .On('=', p => scene.Node(p, we, Be(p, we), "pipe"))
      .On('|', p => scene.Node(p, ns, Be(p, ns), "pipe"))
      .On('I', p => scene.Node(p, ud, Be(p, ud), "pipe"))
      .On('#', p => scene.Block(p, cap));
  }
}

/// <summary>
/// A constructed, fired Cornish boiler placed into a shared <see cref="Scene"/> - the integration-test
/// counterpart of <see cref="BoilerRig"/> (which owns its own world). It registers the real production
/// tick (so <see cref="Scene.Step"/> drives it) and exposes the operating state for setup/assertions.
/// </summary>
internal sealed class BoilerFixture {
  public readonly BlockEntityBoilerCornish Be;
  public readonly BlockBoilerCornish Block;

  public BoilerFixture(
    Scene scene,
    BlockPos pos,
    int blockId = 10,
    int coalId = 11
  ) {
    Block = TestBlocks.Configure(
      new BlockBoilerCornish(),
      "ppex:boiler-cornish-north",
      blockId,
      ("side", "north")
    );
    Be = new BlockEntityBoilerCornish { Pos = pos.Copy(), Block = Block };

    // Force-construct BEFORE Initialize so the production tick registers (AutoStartProduction reads
    // StructureComplete); Initialize then clears _rcc from the absent behaviors, so re-apply after.
    BoilerFakes.ForceConstructed(Be);
    scene.Machine(pos, Block, Be);
    BoilerFakes.ForceConstructed(Be);

    var fuelPos = Block.FuelWorldPos(pos);
    scene.World.Place(
      fuelPos,
      TestBlocks.Configure(new Block(), "game:coalpile", coalId),
      BoilerFakes.BurningPile(fuelPos)
    );
  }

  public BoilerFixture Prime(BoilerState state, float water, float steam) {
    ReflectionHelpers.SetField(Be, "_state", state);
    ReflectionHelpers.SetField(Be, "_waterVolume", water);
    ReflectionHelpers.SetField(Be, "_steamVolume", steam);
    return this;
  }

  public float SteamVolume =>
    (float)ReflectionHelpers.GetField(Be, "_steamVolume")!;

  /// <summary>World cell of the steam pipe that attaches above the outlet connector.</summary>
  public BlockPos SteamPipeAttachPos =>
    Block.SteamPipeWorldPos(Be.Pos).UpCopy();
}

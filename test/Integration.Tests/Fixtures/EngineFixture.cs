using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// A constructed Watt engine in a shared <see cref="Scene"/>, wired with a sealed steam inlet pipe and
/// a fluid-pump sub-machine at its sub-machine cell - the minimum to drive the engine's power tick
/// (it engages only when a sub-machine demands power). Geometry comes from the real
/// <see cref="BlockEngineWatt"/> (offsets fall back to coded defaults with no JSON): north-facing, so
/// the steam inlet is south and the sub-machine sits two cells north.
/// </summary>
internal sealed class EngineFixture {
  public readonly BlockEntityEngineWatt Engine;
  public readonly BlockEngineWatt Block;
  public readonly BlockEntityEngineFluidPump Pump;

  private readonly Scene _scene;
  private readonly BlockPos _inletPipe;

  public EngineFixture(Scene scene, BlockPos pos) {
    _scene = scene;

    Block = TestBlocks.Configure(
      new BlockEngineWatt(),
      "ppex:enginewatt-north",
      20,
      ("side", "north")
    );
    Engine = new BlockEntityEngineWatt { Pos = pos.Copy(), Block = Block };
    scene.Machine(pos, Block, Engine); // Initialize registers the production tick
    RccFake.Complete(Engine); // re-apply: Initialize cleared _rcc from absent behaviors

    // Sealed single-cell steam inlet on the south face: north end abuts the engine's connector,
    // south end capped, so a produced charge holds its pressure instead of leaking.
    _inletPipe = pos.AddCopy(0, 0, 1);
    var nsPipe = PipeTestWorld.MakePipe(orientation: "ns", id: 21);
    scene.Node(
      _inletPipe,
      nsPipe,
      new BlockEntityPipe { Pos = _inletPipe.Copy(), Block = nsPipe },
      "pipe"
    );
    scene.Block(pos.AddCopy(0, 0, 2), PpexScenes.Cap(98));

    // Fluid-pump sub-machine at the engine's sub-machine cell (provides the power demand).
    BlockPos subPos = Block.SubmachinePos(pos);
    var pumpBlock = TestBlocks.Configure(
      new BlockEngineFluidPump(),
      "ppex:enginefluidpump-east",
      22,
      ("side", "east")
    );
    Pump = new BlockEntityEngineFluidPump {
      Pos = subPos.Copy(),
      Block = pumpBlock,
    };
    scene.Machine(subPos, pumpBlock, Pump);
  }

  /// <summary>Charges the inlet steam network to <paramref name="atm"/> (a single 30 L pipe).</summary>
  public EngineFixture SetInletPressure(float atm) {
    _scene
      .NetworkAt<PipeNetwork>(_inletPipe)!
      .TryProduceGas(
        atm * 30f,
        150f,
        "Steam",
        _scene.World.Accessor,
        maxOutputPressure: atm
      );
    return this;
  }

  public float InletVolume =>
    _scene.NetworkAt<PipeNetwork>(_inletPipe)!.State?.Volume ?? 0f;
}

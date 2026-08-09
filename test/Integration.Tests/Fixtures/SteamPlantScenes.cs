using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using SmexAirBlowerBe = SteelmakingExpanded.BlockStructures.Engine.BlockEntities.BlockEntityEngineAirBlower;
using SmexAirBlowerBlock = SteelmakingExpanded.BlockStructures.Engine.Blocks.BlockEngineAirBlower;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Whole-plant fixtures that stand a steam engine up with its real peripherals in a shared
/// <see cref="Scene"/>, so scenario tests can model the in-game setups end to end (steam in → power →
/// work out). Shared helper to wire the engine + its sealed steam inlet; subclasses add the
/// sub-machine and its lines.
/// </summary>
internal static class EnginePlant {
  /// <summary>One oriented, network-joined pipe cell in the scene (steel holds the high-pressure lines).</summary>
  public static void Pipe(
    Scene scene,
    BlockPos pos,
    string orientation,
    int id,
    string material = "iron"
  ) {
    var block = PipeTestWorld.MakePipe(
      material: material,
      orientation: orientation,
      id: id
    );
    scene.Node(
      pos,
      block,
      new BlockEntityPipe { Pos = pos.Copy(), Block = block },
      "pipe"
    );
  }

  /// <summary>The pipe axis ("ns"/"we") that lies along <paramref name="face"/>.</summary>
  public static string Axis(BlockFacing face) =>
    face == BlockFacing.NORTH || face == BlockFacing.SOUTH ? "ns"
    : face == BlockFacing.EAST || face == BlockFacing.WEST ? "we"
    : "ud";
}

/// <summary>
/// Models the starter steam-power setup's water half: a constructed Watt engine driving a fluid-pump
/// sub-machine. The pump's source line (below) holds a fluid intake over a pond; its output line
/// (left) is a sealed water main. With steam at the engine's inlet the engine engages, the pump draws
/// from the pond and lifts water into the main - the boiler→engine→pump→water chain.
/// </summary>
internal sealed class WaterPumpPlant {
  public readonly BlockEntityEngineWatt Engine;
  public readonly BlockEntityEngineFluidPump Pump;
  public readonly BlockEntityFluidIntake Intake;

  private readonly Scene _scene;
  private readonly BlockPos _inlet;
  private readonly BlockPos _pond;
  private readonly BlockPos _output;

  public WaterPumpPlant(Scene scene, BlockPos pos) {
    _scene = scene;

    var engineBlock = TestBlocks.Configure(
      new BlockEngineWatt(),
      "ppex:enginewatt-north",
      30,
      ("side", "north")
    );
    Engine = new BlockEntityEngineWatt {
      Pos = pos.Copy(),
      Block = engineBlock,
    };
    scene.Machine(pos, engineBlock, Engine);
    RccFake.Complete(Engine); // Initialize cleared _rcc from the absent behavior

    // Sealed steam inlet on the engine's south face.
    BlockFacing inletFace = engineBlock.SteamInletFace;
    _inlet = pos.AddCopy(inletFace);
    EnginePlant.Pipe(scene, _inlet, EnginePlant.Axis(inletFace), 31);
    scene.Block(_inlet.AddCopy(inletFace), PpexScenes.Cap(32));

    // Fluid pump at the engine's sub-machine cell (side rotates north→east).
    BlockPos subPos = engineBlock.SubmachinePos(pos);
    var pumpBlock = TestBlocks.Configure(
      new BlockEngineFluidPump(),
      "ppex:enginefluidpump-east",
      33,
      ("side", "east")
    );
    Pump = new BlockEntityEngineFluidPump {
      Pos = subPos.Copy(),
      Block = pumpBlock,
    };
    scene.Machine(subPos, pumpBlock, Pump);

    // Source line: a fluid intake directly below the pump (oriented "u" so it presents a connector up
    // into the pump's DOWN face). It is its own one-cell network - the pond the pump draws from.
    _pond = subPos.DownCopy();
    var intakeBlock = TestBlocks.Configure(
      new BlockFluidIntake(),
      "ppex:fluidintake",
      34,
      ("orientation", "u")
    );
    ReflectionHelpers.SetProperty(intakeBlock, "Orientation", "u");
    Intake = new BlockEntityFluidIntake {
      Pos = _pond.Copy(),
      Block = intakeBlock,
    };
    scene.Node(_pond, intakeBlock, Intake, "pipe");
    // The intake reads water below + resolves its own network when the pump asks it to refill.
    ReflectionHelpers.SetProperty(Intake, nameof(Intake.HasWater), true);
    ReflectionHelpers.SetProperty(
      Intake,
      nameof(Intake.NetworkSystem),
      scene.World.Networks
    );

    // Output line: a sealed water main on the pump's left face.
    BlockFacing leftFace = ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide("east")
    );
    _output = subPos.AddCopy(leftFace);
    EnginePlant.Pipe(scene, _output, EnginePlant.Axis(leftFace), 35);
    scene.Block(_output.AddCopy(leftFace), PpexScenes.Cap(36));
  }

  /// <summary>Tops the engine's steam inlet back up to <paramref name="atm"/> (single 30 L pipe).</summary>
  public WaterPumpPlant Steam(float atm) {
    _scene
      .NetworkAt<PipeNetwork>(_inlet)!
      .TryProduceGas(
        atm * 30f,
        150f,
        "Steam",
        _scene.World.Accessor,
        maxOutputPressure: atm
      );
    return this;
  }

  /// <summary>
  /// Holds the inlet at <paramref name="atm"/> for <paramref name="seconds"/> ticks - a stand-in for
  /// a boiler continuously feeding the line - re-charging before each tick so the running engine has
  /// steam to draw (a sealed pipe's charge would otherwise deplete as the engine consumes it).
  /// </summary>
  public WaterPumpPlant RunWithSteam(float atm, int seconds) {
    for (int i = 0; i < seconds; i++) {
      Steam(atm);
      _scene.Step(1);
    }
    return this;
  }

  /// <summary>Pre-fills the pond (source line) with standing water for the pump to lift.</summary>
  public WaterPumpPlant FillPond(float litres) {
    _scene
      .NetworkAt<PipeNetwork>(_pond)!
      .TryProduceLiquid(litres, 12f, 1f, _scene.World.Accessor);
    return this;
  }

  public float OutputVolume =>
    _scene.NetworkAt<PipeNetwork>(_output)!.State?.Volume ?? 0f;
  public float PondVolume =>
    _scene.NetworkAt<PipeNetwork>(_pond)!.State?.Volume ?? 0f;
  public float InletVolume =>
    _scene.NetworkAt<PipeNetwork>(_inlet)!.State?.Volume ?? 0f;
}

/// <summary>
/// Models the improved steel setup's MP half: a constructed Cornish engine driving an MP-generator
/// sub-machine. With steam in its band the engine engages and delivers a mechanical-power budget
/// (<see cref="BlockEntityEngine.MpPowerBudget"/>) - the boiler→engine→MP-generator chain that powers
/// the converter / helve hammers.
/// </summary>
internal sealed class MpGeneratorPlant {
  public readonly BlockEntityEngineCornish Engine;
  public readonly BlockEntityEngineMpGenerator Generator;

  private readonly Scene _scene;
  private readonly BlockPos _inlet;

  public MpGeneratorPlant(Scene scene, BlockPos pos) {
    _scene = scene;

    var engineBlock = TestBlocks.Configure(
      new BlockEngineCornish(),
      "ppex:enginecornish-north",
      40,
      ("side", "north")
    );
    Engine = new BlockEntityEngineCornish {
      Pos = pos.Copy(),
      Block = engineBlock,
    };
    scene.Machine(pos, engineBlock, Engine);
    RccFake.Complete(Engine);

    BlockFacing inletFace = engineBlock.SteamInletFace;
    _inlet = pos.AddCopy(inletFace);
    // The Cornish engine's band is high (≥6 atm) - an iron pipe bursts at 5, so feed it through steel.
    EnginePlant.Pipe(
      scene,
      _inlet,
      EnginePlant.Axis(inletFace),
      41,
      material: "steel"
    );
    scene.Block(_inlet.AddCopy(inletFace), PpexScenes.Cap(42));

    BlockPos subPos = engineBlock.SubmachinePos(pos);
    var genBlock = TestBlocks.Configure(
      new BlockEngineMPGenerator(),
      "ppex:enginempgenerator-east",
      43,
      ("side", "east")
    );
    Generator = new BlockEntityEngineMpGenerator {
      Pos = subPos.Copy(),
      Block = genBlock,
    };
    scene.Machine(subPos, genBlock, Generator);
  }

  public MpGeneratorPlant Steam(float atm) {
    _scene
      .NetworkAt<PipeNetwork>(_inlet)!
      .TryProduceGas(
        atm * 30f,
        150f,
        "Steam",
        _scene.World.Accessor,
        maxOutputPressure: atm
      );
    return this;
  }

  /// <summary>Holds the inlet at <paramref name="atm"/> for <paramref name="seconds"/> ticks (boiler stand-in).</summary>
  public MpGeneratorPlant RunWithSteam(float atm, int seconds) {
    for (int i = 0; i < seconds; i++) {
      Steam(atm);
      _scene.Step(1);
    }
    return this;
  }

  public float MpPowerBudget => Engine.MpPowerBudget;
  public float InletVolume =>
    _scene.NetworkAt<PipeNetwork>(_inlet)!.State?.Volume ?? 0f;
}

/// <summary>
/// Models the hot-blast supply's air half: a Cornish engine driving the smex air-blower sub-machine.
/// With steam in the engine's band the blower pressurises Air on its left network; once that air
/// crosses the blast threshold (<see cref="SmexValues.BlastPressureThreshold"/>) it counts as Blast -
/// the boiler→engine→blower→blast chain that feeds the cowper stoves, blast-furnace tuyeres and the
/// Bessemer converter. The air blower lives in smex but bolts onto a ppex engine like any sub-machine.
/// </summary>
internal sealed class AirBlowerPlant {
  public readonly BlockEntityEngineCornish Engine;
  public readonly SmexAirBlowerBe Blower;

  private readonly Scene _scene;
  private readonly BlockPos _inlet;
  private readonly BlockPos _blast;

  public AirBlowerPlant(Scene scene, BlockPos pos) {
    _scene = scene;

    var engineBlock = TestBlocks.Configure(
      new BlockEngineCornish(),
      "ppex:enginecornish-north",
      45,
      ("side", "north")
    );
    Engine = new BlockEntityEngineCornish {
      Pos = pos.Copy(),
      Block = engineBlock,
    };
    scene.Machine(pos, engineBlock, Engine);
    RccFake.Complete(Engine);

    // The Cornish band is high (≥6 atm) - feed the inlet through steel (iron bursts at 5).
    BlockFacing inletFace = engineBlock.SteamInletFace;
    _inlet = pos.AddCopy(inletFace);
    EnginePlant.Pipe(
      scene,
      _inlet,
      EnginePlant.Axis(inletFace),
      46,
      material: "steel"
    );
    scene.Block(_inlet.AddCopy(inletFace), PpexScenes.Cap(47));

    BlockPos subPos = engineBlock.SubmachinePos(pos);
    var blowerBlock = TestBlocks.Configure(
      new SmexAirBlowerBlock(),
      "smex:engineairblower-east",
      48,
      ("side", "east")
    );
    Blower = new SmexAirBlowerBe { Pos = subPos.Copy(), Block = blowerBlock };
    scene.Machine(subPos, blowerBlock, Blower);

    // Blast line: a sealed steel pipe on the blower's left face, oriented along that axis, so the
    // pressurised air it pushes is held instead of leaking.
    BlockFacing leftFace = ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide("east")
    );
    _blast = subPos.AddCopy(leftFace);
    EnginePlant.Pipe(
      scene,
      _blast,
      EnginePlant.Axis(leftFace),
      49,
      material: "steel"
    );
    scene.Block(_blast.AddCopy(leftFace), PpexScenes.Cap(50));
  }

  public AirBlowerPlant Steam(float atm) {
    _scene
      .NetworkAt<PipeNetwork>(_inlet)!
      .TryProduceGas(
        atm * 30f,
        150f,
        "Steam",
        _scene.World.Accessor,
        maxOutputPressure: atm
      );
    return this;
  }

  /// <summary>Holds the inlet at <paramref name="atm"/> for <paramref name="seconds"/> ticks (boiler stand-in).</summary>
  public AirBlowerPlant RunWithSteam(float atm, int seconds) {
    for (int i = 0; i < seconds; i++) {
      Steam(atm);
      _scene.Step(1);
    }
    return this;
  }

  public PipeNetwork? BlastNet => _scene.NetworkAt<PipeNetwork>(_blast);
  public string BlastMedium => BlastNet?.State?.MediumType ?? "";
  public float BlastPressure => BlastNet?.State?.Pressure ?? 0f;
  public float BlastVolume => BlastNet?.State?.Volume ?? 0f;
}

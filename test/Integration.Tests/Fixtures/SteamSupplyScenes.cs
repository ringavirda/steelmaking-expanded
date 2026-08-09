using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using PipesAndPowerExpanded.BlockStructures.ManualPump.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Models the documented starter-setup detail that a Cornish boiler can out-pressure a Watt engine, so
/// its steam main is gated through a <see cref="BlockEntityPressureValve"/>: a sealed steam main feeds
/// the engine's inlet, and a relief valve on the far end bleeds anything above its gate into a drain
/// line. Charge the main hard (boiler over-pressure stand-in) and the valve keeps the line near its
/// gate, so the engine runs in its band instead of climbing toward a burst.
/// </summary>
internal sealed class RegulatedEnginePlant {
  public readonly BlockEntityEngineWatt Engine;
  public readonly BlockEntityPressureValve Valve;

  private readonly Scene _scene;
  private readonly BlockPos _main;
  private readonly BlockPos _drain;

  /// <summary>The engine's placed block, for a test that needs one of its connector faces.</summary>
  public BlockEngineWatt EngineBlock => (BlockEngineWatt)Engine.Block;

  public RegulatedEnginePlant(Scene scene, BlockPos enginePos, float gateAtm) {
    _scene = scene;

    var engineBlock = TestBlocks.Configure(
      new BlockEngineWatt(),
      "ppex:enginewatt-north",
      55,
      ("side", "north")
    );
    Engine = new BlockEntityEngineWatt {
      Pos = enginePos.Copy(),
      Block = engineBlock,
    };
    scene.Machine(enginePos, engineBlock, Engine);
    RccFake.Complete(Engine);

    // Steam main: two steel pipes running from the engine's inlet face toward the valve. The engine's
    // block caps the north end, the valve caps the south end, so the run is sealed and holds pressure.
    BlockFacing inletFace = engineBlock.SteamInletFace; // south for a north engine
    BlockPos m0 = enginePos.AddCopy(inletFace);
    BlockPos m1 = m0.AddCopy(inletFace);
    string mainAxis = EnginePlant.Axis(inletFace);
    EnginePlant.Pipe(scene, m0, mainAxis, 56, material: "steel");
    EnginePlant.Pipe(scene, m1, mainAxis, 57, material: "steel");
    _main = m0;

    // Relief valve south of the main: input face points back north at the main, output face south at
    // the drain. It caps the main's south end (non-air) and bridges the main to the drain network.
    string orient = $"{inletFace.Opposite.Code[0]}{inletFace.Code[0]}";
    var valveBlock = TestBlocks.Configure(
      new BlockPressureValve(),
      $"ppex:pressurevalve-steel-{orient}",
      58,
      ("material", "steel"),
      ("type", "pressurevalve"),
      ("orientation", orient)
    );
    ReflectionHelpers.SetProperty(valveBlock, "Type", "pressurevalve");
    ReflectionHelpers.SetProperty(valveBlock, "Orientation", orient);
    BlockPos vPos = m1.AddCopy(inletFace);
    Valve = new BlockEntityPressureValve {
      Pos = vPos.Copy(),
      Block = valveBlock,
    };
    scene.Machine(vPos, valveBlock, Valve); // place + Initialize (registers the relief tick); not a node
    ReflectionHelpers.SetProperty(
      Valve,
      nameof(Valve.NetworkSystem),
      scene.World.Networks
    );
    SetGate(gateAtm);

    // Drain line on the valve's output face - the relief sink, capped so it reads its own pressure.
    BlockPos d0 = vPos.AddCopy(inletFace);
    EnginePlant.Pipe(scene, d0, mainAxis, 59, material: "steel");
    scene.Block(d0.AddCopy(inletFace), PpexScenes.Cap(60));
    _drain = d0;

    // Fluid-pump sub-machine so the engine has a power demand (it only engages with one).
    BlockPos subPos = engineBlock.SubmachinePos(enginePos);
    var pumpBlock = TestBlocks.Configure(
      new BlockEngineFluidPump(),
      "ppex:enginefluidpump-east",
      61,
      ("side", "east")
    );
    scene.Machine(
      subPos,
      pumpBlock,
      new BlockEntityEngineFluidPump { Pos = subPos.Copy(), Block = pumpBlock }
    );
  }

  /// <summary>Dials the valve's gate to <paramref name="atm"/> (stepping from its 1 atm default).</summary>
  public void SetGate(float atm) {
    // Walk the gate up/down in the valve's real 0.25 atm steps so the clamp logic is exercised.
    int guard = 0;
    while (
      Valve.GatePressure < atm - 0.001f
      && Valve.AdjustGatePressure(true)
      && guard++ < 200
    ) { }
    while (
      Valve.GatePressure > atm + 0.001f
      && Valve.AdjustGatePressure(false)
      && guard++ < 200
    ) { }
  }

  /// <summary>Charges the steam main to <paramref name="atm"/> (the boiler's over-pressure).</summary>
  public RegulatedEnginePlant Charge(float atm) {
    _scene
      .NetworkAt<PipeNetwork>(_main)!
      .TryProduceGas(
        atm * 60f,
        150f,
        "Steam",
        _scene.World.Accessor,
        maxOutputPressure: atm
      );
    return this;
  }

  /// <summary>
  /// Holds the main at <paramref name="atm"/> for <paramref name="seconds"/> ticks - a boiler
  /// continuously over-pressuring the line - re-charging before each tick so the running engine and
  /// the relief valve both act on a fed main (a sealed run would otherwise deplete as the engine draws).
  /// </summary>
  public RegulatedEnginePlant RunCharged(float atm, int seconds) {
    for (int i = 0; i < seconds; i++) {
      Charge(atm);
      _scene.Step(1);
    }
    return this;
  }

  public float MainPressure =>
    _scene.NetworkAt<PipeNetwork>(_main)!.State?.Pressure ?? 0f;
  public float DrainVolume =>
    _scene.NetworkAt<PipeNetwork>(_drain)!.State?.Volume ?? 0f;
  public float InletPressure => Engine.InletPressure;
}

/// <summary>
/// Models the engine-free way to start a water loop (handbook starter setup, step 2): a hand-cranked
/// <see cref="BlockEntityManualFluidPump"/> drawing from a pond intake on its input line and lifting
/// water at a fixed 1 atm into an output main - the line you run up into a boiler before any steam
/// engine exists. The intake is the generator; the pump only moves what stands in the input line.
/// </summary>
internal sealed class ManualPumpPlant {
  public readonly BlockEntityManualFluidPump Pump;
  public readonly BlockEntityFluidIntake Intake;

  private readonly Scene _scene;
  private readonly BlockPos _pond;
  private readonly BlockPos _output;

  public ManualPumpPlant(Scene scene, BlockPos pos) {
    _scene = scene;

    var pumpBlock = TestBlocks.Configure(
      new Block(),
      "ppex:manualfluidpump-north",
      62,
      ("side", "north")
    );
    Pump = new BlockEntityManualFluidPump {
      Pos = pos.Copy(),
      Block = pumpBlock,
    };
    scene.Machine(pos, pumpBlock, Pump); // Initialize registers the crank work tick

    int angle = ExOrientation.AngleFromSide("north");
    BlockFacing inFace = ExOrientation.RotateFacing(BlockFacing.SOUTH, angle);
    BlockFacing outFace = ExOrientation.RotateFacing(BlockFacing.NORTH, angle);

    // Pond intake on the input face (presents a connector back at the pump).
    _pond = pos.AddCopy(inFace);
    var intakeBlock = TestBlocks.Configure(
      new BlockFluidIntake(),
      "ppex:fluidintake",
      63,
      ("orientation", inFace.Opposite.Code[..1])
    );
    ReflectionHelpers.SetProperty(
      intakeBlock,
      "Orientation",
      inFace.Opposite.Code[..1]
    );
    Intake = new BlockEntityFluidIntake {
      Pos = _pond.Copy(),
      Block = intakeBlock,
    };
    scene.Node(_pond, intakeBlock, Intake, "pipe");
    ReflectionHelpers.SetProperty(Intake, nameof(Intake.HasWater), true);
    ReflectionHelpers.SetProperty(
      Intake,
      nameof(Intake.NetworkSystem),
      scene.World.Networks
    );

    // Output main on the delivery face - the line that climbs into the boiler.
    _output = pos.AddCopy(outFace);
    string axis = EnginePlant.Axis(outFace);
    EnginePlant.Pipe(scene, _output, axis, 64);
    scene.Block(_output.AddCopy(outFace), PpexScenes.Cap(65));
  }

  /// <summary>Pre-fills the pond's input line with standing water for the pump to lift.</summary>
  public ManualPumpPlant FillPond(float litres) {
    _scene
      .NetworkAt<PipeNetwork>(_pond)!
      .TryProduceLiquid(litres, 20f, 1f, _scene.World.Accessor);
    return this;
  }

  /// <summary>Cranks the pump for <paramref name="seconds"/> ticks (a player holding right-click).</summary>
  public ManualPumpPlant Crank(int seconds) {
    Pump.OnPumpStart();
    for (int i = 0; i < seconds; i++) {
      Pump.OnPumpStep(); // refresh the watchdog as a held button would
      _scene.Step(1);
    }
    return this;
  }

  public float OutputVolume =>
    _scene.NetworkAt<PipeNetwork>(_output)!.State?.Volume ?? 0f;
  public bool OutputIsWater =>
    _scene.NetworkAt<PipeNetwork>(_output)!.State?.IsLiquid ?? false;
}

/// <summary>
/// Models the steam plant's closed water loop's recovery leg (handbook starter setup, step 5): a
/// <see cref="BlockEntitySteamCondenser"/> takes the engine's spent steam off the north line and
/// condenses it into the water passing W→E, sending the recovered water (condensate + through-flow)
/// on toward the boiler instead of venting it. North = spent-steam line, west = feed water, east =
/// the recovered-water line back to the boiler.
/// </summary>
internal sealed class CondenserPlant {
  public readonly BlockEntitySteamCondenser Condenser;

  private readonly Scene _scene;
  private readonly BlockPos _steam;
  private readonly BlockPos _feed;
  private readonly BlockPos _recovered;

  public CondenserPlant(Scene scene, BlockPos pos) {
    _scene = scene;

    var block = TestBlocks.Configure(
      new PipesAndPowerExpanded.BlockNetworkPipe.Blocks.BlockSteamCondenser(),
      "ppex:steamcondenser-north",
      66,
      ("side", "north")
    );
    Condenser = new BlockEntitySteamCondenser {
      Pos = pos.Copy(),
      Block = block,
    };
    scene.Machine(pos, block, Condenser); // Initialize registers the condense tick

    // North: the spent-steam line.
    _steam = pos.AddCopy(BlockFacing.NORTH);
    EnginePlant.Pipe(scene, _steam, "ns", 67);
    scene.Block(_steam.AddCopy(BlockFacing.NORTH), PpexScenes.Cap(68));

    // West: the feed-water line (the fuller side, so it reads as the inlet).
    _feed = pos.AddCopy(BlockFacing.WEST);
    EnginePlant.Pipe(scene, _feed, "we", 69);
    scene.Block(_feed.AddCopy(BlockFacing.WEST), PpexScenes.Cap(71));

    // East: the recovered-water line back toward the boiler (starts empty -> the outlet).
    _recovered = pos.AddCopy(BlockFacing.EAST);
    EnginePlant.Pipe(scene, _recovered, "we", 72);
    scene.Block(_recovered.AddCopy(BlockFacing.EAST), PpexScenes.Cap(73));
  }

  public CondenserPlant ChargeSteam(float litres) {
    _scene
      .NetworkAt<PipeNetwork>(_steam)!
      .TryProduceGas(
        litres,
        150f,
        "Steam",
        _scene.World.Accessor,
        maxOutputPressure: 20f
      );
    return this;
  }

  public CondenserPlant ChargeFeedWater(float litres) {
    _scene
      .NetworkAt<PipeNetwork>(_feed)!
      .TryProduceLiquid(litres, 40f, 1f, _scene.World.Accessor);
    return this;
  }

  public bool Condensing =>
    (bool)ReflectionHelpers.GetField(Condenser, "_condensing")!;
  public float SteamVolume =>
    _scene.NetworkAt<PipeNetwork>(_steam)!.State?.Volume ?? 0f;
  public float RecoveredVolume =>
    _scene.NetworkAt<PipeNetwork>(_recovered)!.State?.Volume ?? 0f;
  public bool RecoveredIsWater =>
    _scene.NetworkAt<PipeNetwork>(_recovered)!.State?.IsLiquid ?? false;
}

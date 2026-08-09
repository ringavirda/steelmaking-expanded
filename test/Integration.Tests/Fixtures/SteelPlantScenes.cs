using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.Tests;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using SteelmakingExpanded.BlockStructures.Converter.Blocks;
using SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Models the Bessemer steelmaking line (handbook bessemer article) end to end: a converter control
/// fed molten iron from an input canal cell, blown with real <strong>blast</strong> drawn off a live
/// gas network through its intake port, refining the charge to steel and pouring it into an output
/// canal cell. It stands up the three services the converter actually reads each tick - the input
/// cell, the output cell, and an Air≥blast-threshold pipe network across the intake - so the refining
/// process runs against a real blast supply (the per-state steps are driven directly, as the gated
/// <c>OnProductionTick</c> also requires an aligned transmission + constructed vessel).
/// </summary>
internal sealed class ConverterRig {
  private const string Iron = "game:ingot-iron";
  private const string Steel = "game:ingot-steel";

  // Structure-local peripheral offsets (mirror the control's private constants).
  private static readonly (int x, int y, int z) InputTapLocal = (1, 1, 2);
  private static readonly (int x, int y, int z) OutputStartLocal = (1, -2, 2);
  private static readonly (int x, int y, int z) GasIntakeLocal = (0, 0, 4);
  private static readonly (int x, int y, int z) TransmissionLocal = (0, -1, 0);

  public readonly TestWorld World;
  public readonly BlockEntityConverterControl Control;
  public readonly BlockEntityMoltenCanal Input;
  public readonly BlockEntityMoltenCanal Output;
  public readonly BlockEntityConverterTransmission Transmission;

  private readonly PipeNetwork _blast;

  public ConverterRig() {
    World = new TestWorld();
    World.RegisterItem(Iron, 1500f);
    World.RegisterItem(Steel, 1500f);
    World.RegisterItem("game:metalbit-iron");
    World.RegisterNetwork("pipe", s => new PipeNetwork(s));

    Control = new BlockEntityConverterControl {
      Pos = new BlockPos(0, 8, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:converterbessemercontrol-north",
        1,
        ("side", "north")
      ),
    };
    World.Attach(Control);
    // Establish the structure angle so the peripheral offsets resolve.
    ReflectionHelpers.Invoke(Control, "UpdateStructureRotation");

    Input = PlaceCell(InputTapLocal, 9);
    Output = PlaceCell(OutputStartLocal, 10);

    // Blast intake port + the Air pipe network docked against its connector face.
    BlockPos intakePos = GlobalPos(GasIntakeLocal);
    var intakeBlock = TestBlocks.Configure(
      new BlockConverterIntake(),
      "smex:converterintake-north",
      11,
      ("side", "north")
    );
    World.Place(intakePos, intakeBlock);

    BlockFacing connFace = ((BlockConverterIntake)intakeBlock).ConnectorFace;
    BlockPos blastPos = intakePos.AddCopy(connFace);
    var blastPipe = PipeTestWorld.MakePipe(orientation: "ns", id: 12);
    var blastBe = new BlockEntityPipe {
      Pos = blastPos.Copy(),
      Block = blastPipe,
    };
    World.Place(blastPos, blastPipe, blastBe);
    World.Attach(blastBe);
    World.AddNode(blastPos, "pipe");
    _blast = (PipeNetwork)World.NetworkAt(blastPos)!;

    // Mechanical transmission below the control - the converter reads its turning network for power.
    BlockPos transPos = GlobalPos(TransmissionLocal);
    var transBlock = TestBlocks.Configure(
      new BlockConverterTransmission(),
      "smex:converterbessemertransmission-north",
      13,
      ("side", "north")
    );
    Transmission = new BlockEntityConverterTransmission {
      Pos = transPos.Copy(),
      Block = transBlock,
    };
    World.Place(transPos, transBlock, Transmission);
    World.Attach(Transmission);
  }

  /// <summary>
  /// Drives the transmission's mechanical network at <paramref name="speed"/> (the engine→generator→
  /// axle chain spinning it). Attaches the real MP behavior + a faked turning network so the control's
  /// <see cref="BlockEntityConverterControl.HasPower"/> reads it.
  /// </summary>
  public ConverterRig SetMechPower(float speed) {
    var mp = new BEBehaviorMPConverterTransmission(Transmission);
    MechPower.Attach(Transmission, mp, MechPower.Network(speed));
    return this;
  }

  /// <summary>Whether the converter sees mechanical power from its transmission.</summary>
  public bool HasPower => Control.HasPower();

  private BlockPos GlobalPos((int x, int y, int z) l) =>
    (BlockPos)ReflectionHelpers.Invoke(Control, "GetGlobalPos", l.x, l.y, l.z)!;

  private BlockEntityMoltenCanal PlaceCell((int x, int y, int z) local, int id) {
    BlockPos pos = GlobalPos(local);
    var cell = new BlockEntityMoltenCanal {
      Pos = pos.Copy(),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanal-straight-ns",
        id,
        ("type", "straight"),
        ("orientation", "ns")
      ),
    };
    World.Place(pos, cell.Block, cell);
    World.Attach(cell);
    return cell;
  }

  private static ItemStack MetalStack(
    TestWorld world,
    string code,
    float temp
  ) => MoltenMetal.CreateStack(world.World, code, temp)!;

  /// <summary>Pours molten iron into the input canal cell (the furnace tap feeding the converter).</summary>
  public ConverterRig PourIronToInput(int units, float temp = 1700f) {
    Input.PushMetal(units, MetalStack(World, Iron, temp), World.World);
    return this;
  }

  /// <summary>Charges the intake's gas network with blast (Air at <paramref name="atm"/> ≥ 2.5 atm).</summary>
  public ConverterRig ChargeBlast(float atm = 3f) {
    _blast.TryProduceGas(
      atm * 30f,
      20f,
      "Air",
      World.Accessor,
      maxOutputPressure: atm
    );
    return this;
  }

  public ConverterRig Fill() => Invoke("TickFilling");

  /// <summary>
  /// One refining tick (consumes blast, holds temperature, advances the process clock). The blast
  /// pressure is sampled the way the production tick samples it, so the rig reads the same intake
  /// the machine would.
  /// </summary>
  public ConverterRig Refine() {
    float pressure = (float)ReflectionHelpers.Invoke(Control, "BlastPressure")!;
    ReflectionHelpers.Invoke(Control, "TickNormal", 1f, pressure);
    return this;
  }

  public ConverterRig Pour() => Invoke("TickPouring");

  /// <summary>Jumps the refining clock to just before completion so the next <see cref="Refine"/> finishes it.</summary>
  public ConverterRig FastForwardToAlmostDone() {
    ReflectionHelpers.SetField(
      Control,
      "_processSeconds",
      SmexValues.BessemerProcessDuration - 0.5f
    );
    return this;
  }

  private ConverterRig Invoke(string method) {
    ReflectionHelpers.Invoke(Control, method, 1f);
    return this;
  }

  public int ContentUnits =>
    (int)ReflectionHelpers.GetField(Control, "_contentUnits")!;
  public float ProcessSeconds =>
    (float)ReflectionHelpers.GetField(Control, "_processSeconds")!;
  public float BlastVolume => _blast.State?.Volume ?? 0f;

  public string ContentCode =>
    ReflectionHelpers.GetField(Control, "_content") is ItemStack s
      ? s.Collectible.Code.ToString()
      : "";
}

/// <summary>
/// Models the cowper stove's regenerator cycle (handbook hot-blast article): a single stove
/// <strong>charges</strong> its brick core from hot furnace exhaust piped to its intake, then
/// <strong>discharges</strong> by passing cool blast air through it - the air leaves scorching hot
/// (hot blast for the furnace tuyeres) and the core gives its heat up. Wires the stove's exhaust
/// intake (charge), and the air passthrough + hot-air outlet it reads on discharge.
/// </summary>
internal sealed class CowperRig {
  public readonly TestWorld World;
  public readonly BlockEntityCowperStove Stove;

  private readonly PipeNetwork _exhaust;
  private readonly BlockEntityPipePassthrough _airIn;
  private readonly PipeNetwork _airInNet;
  private readonly PipeNetwork _hotOut;

  public CowperRig() {
    World = new TestWorld();
    World.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var pos = new BlockPos(0, 8, 0);
    Stove = new BlockEntityCowperStove {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:cowperstove-north",
        1,
        ("side", "north")
      ),
    };
    World.Attach(Stove);
    ReflectionHelpers.Invoke(Stove, "UpdateStructureRotation");
    // Prime the config-cached tunables Initialize would set (Initialize isn't run headlessly).
    ReflectionHelpers.SetField(
      Stove,
      "_intakeVolume",
      SmexValues.CowperIntakeVolume
    );
    ReflectionHelpers.SetField(
      Stove,
      "_factorDefault",
      SmexValues.CowperHeatingSpeedDefault
    );
    ReflectionHelpers.SetField(
      Stove,
      "_factorOtherCoal",
      SmexValues.CowperHeatingSpeedOtherCoal
    );
    ReflectionHelpers.SetField(
      Stove,
      "_factorAnthracite",
      SmexValues.CowperHeatingSpeedAnthracite
    );
    ReflectionHelpers.SetField(
      Stove,
      "_coolingSpeedExhaust",
      SmexValues.CowperCoolingSpeedExhaust
    );
    ReflectionHelpers.SetField(
      Stove,
      "_coolingSpeedAir",
      SmexValues.CowperCoolingSpeedAir
    );
    ReflectionHelpers.SetField(
      Stove,
      "_maxTemperature",
      SmexValues.CowperMaxTemperature
    );
    ReflectionHelpers.SetProperty(Stove, "StructureComplete", true);
    ReflectionHelpers.SetField(Stove, "_connectorFace", BlockFacing.SOUTH);

    // Charge side: a sealed exhaust run butted against the stove's south intake face.
    _exhaust = SealedRunOn(pos, BlockFacing.SOUTH, 2);

    // Discharge side: a cool-air passthrough at the stove's air-intake cell, and a hot-air outlet
    // pipe at the hot-blast cell - both resolved from the stove's structure-local offsets.
    BlockPos airInPos = GlobalPos(0, 1, 2);
    var ptBlock = PipeTestWorld.MakePipe(orientation: "ns", id: 20);
    _airIn = new BlockEntityPipePassthrough {
      Pos = airInPos.Copy(),
      Block = ptBlock,
    };
    World.Place(airInPos, ptBlock, _airIn);
    World.Attach(_airIn);
    World.AddNode(airInPos, "pipe");
    ReflectionHelpers.SetProperty(
      _airIn,
      nameof(_airIn.NetworkSystem),
      World.Networks
    );
    _airInNet = (PipeNetwork)World.NetworkAt(airInPos)!;

    BlockPos hotOutPos = GlobalPos(0, 1, 0);
    var outBlock = PipeTestWorld.MakePipe(orientation: "ns", id: 21);
    var outBe = new BlockEntityPipe {
      Pos = hotOutPos.Copy(),
      Block = outBlock,
    };
    World.Place(hotOutPos, outBlock, outBe);
    World.Attach(outBe);
    World.AddNode(hotOutPos, "pipe");
    // Attach (not Initialize) leaves NetworkSystem unset; the outlet needs it to produce into its net.
    ReflectionHelpers.SetProperty(
      outBe,
      nameof(outBe.NetworkSystem),
      World.Networks
    );
    _hotOut = (PipeNetwork)World.NetworkAt(hotOutPos)!;
  }

  private BlockPos GlobalPos(int x, int y, int z) =>
    (BlockPos)ReflectionHelpers.Invoke(Stove, "GetGlobalPos", x, y, z)!;

  /// <summary>A sealed 2-cell pipe run butted against <paramref name="face"/> of <paramref name="at"/>.</summary>
  private PipeNetwork SealedRunOn(BlockPos at, BlockFacing face, int firstId) {
    var pipe = PipeTestWorld.MakePipe(orientation: "ns", id: firstId);
    BlockPos p1 = at.AddCopy(face);
    BlockPos p2 = p1.AddCopy(face);
    World.Place(p1, pipe);
    World.Place(p2, pipe);
    World.Place(
      p2.AddCopy(face),
      TestBlocks.Configure(new Block(), "game:rock", 98)
    ); // far cap
    World.Place(at, TestBlocks.Configure(new Block(), "game:rock", 99)); // near cap (stove cell)
    World.AddNode(p1, "pipe");
    World.AddNode(p2, "pipe");
    return (PipeNetwork)World.NetworkAt(p1)!;
  }

  /// <summary>Charges the exhaust intake with hot furnace exhaust, then runs one production tick.</summary>
  public CowperRig ChargeFromExhaust(float exhaustTemp, float litres = 60f) {
    _exhaust.TryProduceGas(
      litres,
      exhaustTemp,
      "Exhaust",
      World.Accessor,
      maxOutputPressure: 10f
    );
    Tick();
    return this;
  }

  /// <summary>
  /// Feeds cool blast air into the passthrough, then runs one production tick (discharge). The exhaust
  /// line is valved off first (drained) - a stove only discharges while it is NOT taking exhaust, the
  /// real two-stove charge/discharge swap.
  /// </summary>
  public CowperRig DischargeAir(float airTemp = 20f, float litres = 60f) {
    _exhaust.TryConsumeGas(float.MaxValue, World.Accessor); // valve the exhaust off
    _airInNet.TryProduceGas(
      litres,
      airTemp,
      "Air",
      World.Accessor,
      maxOutputPressure: 3f
    );
    // The stove reads the passthrough's client-synced Volume/Medium/Temperature, so push the network
    // state into those display fields (a broadcast) before the tick.
    _airInNet.BroadcastUpdate(World.Accessor);
    Tick();
    return this;
  }

  /// <summary>
  /// Both valves open: pumps fresh blast air into the passthrough AND hot exhaust into the intake on
  /// the same tick - the genuine "mixing" misconfiguration the stove must refuse to charge through.
  /// </summary>
  public CowperRig MixAirAndExhaust(
    float exhaustTemp = 1200f,
    float airLitres = 60f,
    float exhaustLitres = 60f
  ) {
    _airInNet.TryProduceGas(
      airLitres,
      20f,
      "Air",
      World.Accessor,
      maxOutputPressure: 3f
    );
    _airInNet.BroadcastUpdate(World.Accessor);
    _exhaust.TryProduceGas(
      exhaustLitres,
      exhaustTemp,
      "Exhaust",
      World.Accessor,
      maxOutputPressure: 10f
    );
    Tick();
    return this;
  }

  private void Tick() =>
    ReflectionHelpers.Invoke(Stove, "OnProductionTick", 1f);

  public float CoreTemperature =>
    (float)ReflectionHelpers.GetField(Stove, "_internalTemperature")!;
  public string HotBlastMedium => _hotOut.State?.MediumType ?? "";
  public float HotBlastTemperature => _hotOut.State?.Temperature ?? 0f;
  public float HotBlastVolume => _hotOut.State?.Volume ?? 0f;
}

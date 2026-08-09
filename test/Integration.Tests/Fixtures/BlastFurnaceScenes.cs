using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.Tests;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Drives the blast furnace's primary process headlessly (handbook blast-furnace + hot-blast
/// articles): a charged, lit hearth fed hot blast through its tuyeres climbs past iron's melting
/// point, enters the Melting phase, turns blast mix into molten iron, and taps it into a canal -
/// the steelmaking line's first stage. Stands up the peripherals the gated <c>OnProductionTick</c>
/// reads (hearth blast-mix piles, blast-fed tuyeres) and the iron tap + canal, then drives the tick.
/// Timers are fast-forwarded so the multi-minute melt is reachable in a test.
/// </summary>
internal sealed class BlastFurnaceRig {
  public readonly TestWorld World;
  public readonly BlockEntityBlastFurnace Furnace;
  public BlockEntityMoltenCanalStart? Canal { get; private set; }

  private readonly BlockPos _pos = new(0, 16, 0);
  private readonly PipeNetwork[] _tuyeres;
  private float _blastTemp = -1f;
  private float _blastVolume = 150f;

  public BlastFurnaceRig(int blastMix = 400) {
    World = new TestWorld();
    World.RegisterItem("game:ingot-iron", 1500f);
    World.RegisterItem("smex:slag");
    World.RegisterNetwork("pipe", s => new PipeNetwork(s));

    Furnace = new BlockEntityBlastFurnace {
      Pos = _pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:blastfurnacedoor-north",
        1,
        ("side", "north")
      ),
      BaseAngleRad = 0f,
    };
    World.Place(_pos, Furnace.Block, Furnace);
    World.Attach(Furnace);
    ReflectionHelpers.Invoke(Furnace, "UpdateStructureRotation");
    ReflectionHelpers.Invoke(Furnace, "CacheAttributes");
    ReflectionHelpers.SetProperty(
      Furnace,
      nameof(Furnace.StructureComplete),
      true
    );
    // Initialize (which scans for the gas-outlet/tuyere cells) isn't run headlessly - do that scan
    // so the tick reads the tuyeres we place below.
    ReflectionHelpers.Invoke(Furnace, "ScanForOutlets");

    // Hearth piles holding the blast-mix charge (split across two cells in the hearth box), lit.
    BlastmixPile(_pos.AddCopy(0, 0, 2), blastMix / 2);
    BlastmixPile(_pos.AddCopy(0, -1, 2), blastMix - blastMix / 2);

    // Tuyeres: a pipe at each tuyere cell, each its own blast network.
    _tuyeres =
    [
      Tuyere(_pos.AddCopy(0, -2, 1), 20),
      Tuyere(_pos.AddCopy(0, -2, 3), 21),
    ];
  }

  private void BlastmixPile(BlockPos pos, int units) {
    var pile = new BlockEntityCoalPile { Pos = pos.Copy() };
    var inv = new InventoryGeneric(1, "coalpile", "test", World.Api, null);
    var blastmix = new Item {
      Code = new AssetLocation("smex", "blastmix"),
      ItemId = 4242,
    };
    inv[0].Itemstack = new ItemStack(blastmix, units);
    ReflectionHelpers.SetField(pile, "inventory", inv);
    ReflectionHelpers.SetField(pile, "burning", true);
    World.Place(
      pos,
      TestBlocks.Configure(
        new Block(),
        "game:coalpile",
        50 + pos.Y,
        ("dummy", "x")
      ),
      pile
    );
    World.Attach(pile);
  }

  private PipeNetwork Tuyere(BlockPos pos, int id) {
    var pipe = PipeTestWorld.MakePipe(orientation: "ns", id: id);
    var be = new BlockEntityPipe { Pos = pos.Copy(), Block = pipe };
    World.Place(pos, pipe, be);
    World.Attach(be);
    World.AddNode(pos, "pipe");
    ReflectionHelpers.SetProperty(be, nameof(be.NetworkSystem), World.Networks);
    return (PipeNetwork)World.NetworkAt(pos)!;
  }

  /// <summary>
  /// Puts a one-pipe stub on each gas-outlet cell. A single node holds only one pipe's worth, so it
  /// saturates within a couple of ticks and the flue reads as blocked - the state a player reaches
  /// by valving their cowper stoves off the exhaust main, as the handbook's regenerator swap
  /// requires. Without this the rig has no outlets at all and the choke path never runs.
  /// </summary>
  public BlastFurnaceRig WithBlockedExhaust() {
    Tuyere(_pos.AddCopy(0, 3, 1), 30);
    Tuyere(_pos.AddCopy(0, 3, 3), 31);
    // Seal both stubs. An open end vents a fixed 8 L/s, which frees enough room for the next tick
    // to be accepted - so an unsealed stub only chokes intermittently and the extinguish timer
    // keeps resetting. The player's case is a main whose sink has been valved away: sealed, full,
    // and refusing every tick.
    foreach (var z in new[] { 0, 2, 4 })
      World.Place(
        _pos.AddCopy(0, 3, z),
        TestBlocks.Configure(new Block(), "game:rock", 90 + z)
      );
    return this;
  }

  /// <summary>
  /// Turns the air blowers on: pressurised blast at <paramref name="temp"/> delivered to each tuyere
  /// every tick. <paramref name="litresPerTuyere"/> is what the blowers can actually make - leave it
  /// at the default for an unlimited supply, or set it below the furnace's demand to model a plant
  /// whose blowers cannot keep up.
  /// </summary>
  public BlastFurnaceRig FeedBlast(
    float temp = 950f,
    float litresPerTuyere = 150f
  ) {
    _blastTemp = temp;
    _blastVolume = litresPerTuyere;
    return this;
  }

  /// <summary>Cuts the blast off (the air blowers stopped / the cowpers ran cold).</summary>
  public BlastFurnaceRig CutBlast() {
    _blastTemp = -1f;
    return this;
  }

  /// <summary>Places an open iron tap below the furnace with a canal start under it.</summary>
  public BlastFurnaceRig WithIronTapAndCanal() {
    BlockPos tapPos = Global(2, -2, 2);
    var tap = new BlockEntityBlastFurnaceTap {
      Pos = tapPos.Copy(),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:blastfurnacetap-north",
        30,
        ("side", "north")
      ),
    };
    World.Place(tapPos, tap.Block, tap);
    World.Attach(tap);
    tap.TogglePouring(); // open

    BlockPos canalPos = tapPos.AddCopy(BlockFacing.NORTH.Opposite).DownCopy();
    Canal = new BlockEntityMoltenCanalStart {
      Pos = canalPos.Copy(),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanalstart-ns",
        31,
        ("type", "start"),
        ("orientation", "ns")
      ),
    };
    World.Place(canalPos, Canal.Block, Canal);
    World.Attach(Canal);
    return this;
  }

  private BlockPos Global(int x, int y, int z) =>
    (BlockPos)ReflectionHelpers.Invoke(Furnace, "GetGlobalPos", x, y, z)!;

  /// <summary>Advances the furnace tick <paramref name="ticks"/> times, re-feeding blast each tick.</summary>
  public BlastFurnaceRig Tick(int ticks = 1) {
    for (int i = 0; i < ticks; i++) {
      if (_blastTemp >= 0f)
        foreach (var net in _tuyeres) {
          net.TryProduceGas(
            _blastVolume,
            _blastTemp,
            "Air",
            World.Accessor,
            maxOutputPressure: 5f
          );
          net.BroadcastUpdate(World.Accessor); // push Medium/Pressure/Temperature to the tuyere pipes
        }
      ReflectionHelpers.Invoke(Furnace, "OnProductionTick", 1f);
    }
    return this;
  }

  #region Fast-forward + accessors

  public BlastFurnaceRig SetState(BlastFurnaceState s) {
    ReflectionHelpers.SetProperty(Furnace, nameof(Furnace.State), s);
    return this;
  }

  public BlastFurnaceRig SetTemp(float t) {
    ReflectionHelpers.SetField(Furnace, "_internalTemp", t);
    return this;
  }

  public BlastFurnaceRig SetBelowMeltingSeconds(float s) {
    ReflectionHelpers.SetField(Furnace, "_belowMeltingSeconds", s);
    return this;
  }

  public BlastFurnaceRig SetMeltSeconds(float s) {
    ReflectionHelpers.SetField(Furnace, "_meltSeconds", s);
    return this;
  }

  public BlastFurnaceRig SetMoltenIron(float v) {
    ReflectionHelpers.SetField(Furnace, "_moltenIron", v);
    return this;
  }

  public BlastFurnaceState State => Furnace.State;
  public float Temp =>
    (float)ReflectionHelpers.GetField(Furnace, "_internalTemp")!;

  /// <summary>Blast mix still in the hearth, as the last tick counted it. Falls only when a melt
  /// cycle actually completes, so it is the honest witness for "is this furnace still working".</summary>
  public int MixCount =>
    (int)ReflectionHelpers.GetField(Furnace, "_cachedMixCount")!;

  /// <summary>Seconds accrued toward extinguishing. Stays 0 while nothing counts as a disruption.</summary>
  public float ExtinguishSeconds =>
    (float)ReflectionHelpers.GetField(Furnace, "_extinguishSeconds")!;

  public float MoltenIron =>
    (float)ReflectionHelpers.GetField(Furnace, "_moltenIron")!;
  public int CanalIron => Canal?.CellAmount ?? 0;

  /// <summary>The hearth ceiling the last tick computed from the blast it was fed.</summary>
  public float TargetTemp =>
    (float)ReflectionHelpers.GetField(Furnace, "_targetTemp")!;

  /// <summary>The melt multiplier the last tick ran at - heat margin and air supply combined.</summary>
  public float MeltSpeed =>
    (float)ReflectionHelpers.GetField(Furnace, "_meltSpeed")!;

  /// <summary>Air the tuyeres actually took last tick.</summary>
  public float AirDrawn =>
    (float)ReflectionHelpers.GetField(Furnace, "_airDrawn")!;

  /// <summary>Air the melt asked for last tick.</summary>
  public float AirRequested =>
    (float)ReflectionHelpers.GetField(Furnace, "_airRequested")!;

  #endregion
}

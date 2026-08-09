using System;
using System.Collections.Generic;
using System.Text;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.Helpers;
using SteelmakingExpanded.Patches;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>
/// Block entity for the blast furnace multiblock. Drives the firing/melting state
/// machine: consumes blast mix, draws air/blast through the tuyeres, vents exhaust
/// through the gas outlets, accumulates molten iron and slag, and feeds the taps.
/// </summary>
[BlockEntityRegister]
public class BlockEntityBlastFurnace : BlockEntityMultiblockStructure {
  /// <summary>Whether the exhaust network is full, stalling production.</summary>
  public bool IsChoked { get; private set; }

  /// <summary>Current operating state of the furnace.</summary>
  public BlastFurnaceState State { get; private set; } = BlastFurnaceState.Idle;

  private int _cachedMixCount = 0;
  private bool _cachedIsFull = false;
  private List<BlockPos> _gasOutlets = [];
  private List<BlockPos> _tuyeres = [];

  private float _internalTemp = 20f;

  // Timers accumulate elapsed seconds (dt) so durations are independent of the
  // production-tick interval. Thresholds below are in seconds.
  private float _meltSeconds = 0;
  private float _extinguishSeconds = 0;
  private float _belowMeltingSeconds = 0;
  private float _moltenIron = 0;
  private float _moltenSlag = 0;

  // Last tick's operating point, for the readout.
  private float _targetTemp = 20f;
  private float _meltSpeed = 0f;
  private float _airDrawn = 0f;
  private float _airRequested = 0f;

  /// <summary>Base yaw (radians) of the furnace door, used to orient the multiblock structure.</summary>
  public float BaseAngleRad { get; set; } = -1f;

  // Sound throttles (world-elapsed ms): the furnace fire ambience and the molten
  // tap-pour hiss are looping, gated so the per-second tick doesn't spam audio.
  private long _lastFireSoundMs;
  private long _lastTapSoundMs;

  private string _cachedInfoText = "";
  private long _lastInfoUpdate = 0;

  // Block attributes cached at init instead of re-parsing the JsonObject every tick / HUD refresh.
  private float _ambientTemp;
  private float _ignitionTemp;
  private int _blastMixRequiredToRun;
  private float _disruptionGraceSeconds;
  private float _doorOpenGraceSeconds;
  private float _naturalMaxTemp;
  private float _boostedMaxTemp;
  private float _blastTempReference;
  private float _heatRateBase;
  private float _heatRateHot;
  private float _heatRateUnblown;
  private float _ironMeltingPoint;
  private float _meltStallSeconds;
  private float _meltSpeedBase;
  private float _meltGainPer100C;
  private float _meltSpeedMin;
  private float _meltSpeedMax;
  private float _meltIntervalSec;
  private float _ironPerMeltCycle;
  private float _slagPerMeltCycle;
  private int _blastMixPerMeltCycle;
  private float _maxMoltenIron;
  private float _maxMoltenSlag;
  private float _tuyereIntakeVolume;

  protected override int CompletionTickMs => 3000;

  #region Abstract method implementations

  protected override void UpdateStructureRotation() {
    if (Block == null)
      return;

    if (BaseAngleRad < 0) {
      var doorBehavior = GetBehavior<BEBehaviorDoor>();
      BaseAngleRad = doorBehavior != null ? doorBehavior.RotateYRad : 0;
    }

    float angleDeg = BaseAngleRad * GameMath.RAD2DEG % 360;
    if (angleDeg < 0)
      angleDeg += 360;
    int snappedAngle = (int)System.Math.Round(angleDeg / 90.0) * 90 % 360;
    if (snappedAngle < 0)
      snappedAngle += 360;

    SetStructureAngle(snappedAngle % 360);
  }

  protected override void OnStructureCompleted() => ScanForOutlets();

  protected override void OnStructureLost() {
    if (State != BlastFurnaceState.Idle)
      Extinguish();
  }

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("smex:bf-error-incomplete", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("smex:bf-error-complete");

  #endregion

  #region Initialization

  /// <summary>Forces the structure rotation to be recomputed (call after placement).</summary>
  public void Init() => UpdateStructureRotation();

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    CacheAttributes();
    if (api.Side == EnumAppSide.Server && StructureComplete)
      ScanForOutlets();
  }

  private void CacheAttributes() {
    _ambientTemp = ExClimate.AmbientAt(this);
    _ignitionTemp = SmexValues.BfIgnitionTemperature;
    _blastMixRequiredToRun = SmexValues.BlastMixRequiredToRun;
    _disruptionGraceSeconds = SmexValues.BfDisruptionGraceSeconds;
    _doorOpenGraceSeconds = SmexValues.BfDoorOpenGraceSeconds;
    _naturalMaxTemp = SmexValues.BfNaturalMaxTemp;
    _boostedMaxTemp = SmexValues.BfBoostedMaxTemp;
    _blastTempReference = SmexValues.BfBlastTempReference;
    _heatRateBase = SmexValues.BfHeatRateBase;
    _heatRateHot = SmexValues.BfHeatRateHot;
    _heatRateUnblown = SmexValues.BfHeatRateUnblown;
    _ironMeltingPoint = SmexValues.BfIronMeltingPoint;
    _meltStallSeconds = SmexValues.BfMeltStallSeconds;
    _meltSpeedBase = SmexValues.BfMeltSpeedBase;
    _meltGainPer100C = SmexValues.BfMeltGainPer100C;
    _meltSpeedMin = SmexValues.BfMeltSpeedMin;
    _meltSpeedMax = SmexValues.BfMeltSpeedMax;
    _meltIntervalSec = SmexValues.BfMeltIntervalSec;
    _ironPerMeltCycle = SmexValues.BfIronPerMeltCycle;
    _slagPerMeltCycle = SmexValues.BfSlagPerMeltCycle;
    _blastMixPerMeltCycle = SmexValues.BfBlastMixPerMeltCycle;
    _maxMoltenIron = SmexValues.BfMaxMoltenIron;
    _maxMoltenSlag = SmexValues.BfMaxMoltenSlag;
    _tuyereIntakeVolume = SmexValues.TuyereIntakeVolume;
  }

  #endregion

  #region Molten stack construction

  private ItemStack? CreateMoltenStack(string metalCode, int units, float temp) {
    // Use the item codes the molten network/molds expect downstream: iron as game:ingot-iron,
    // slag as smex:slag.
    AssetLocation loc =
      metalCode == "slag"
        ? new AssetLocation("smex", "slag")
        : new AssetLocation("game", $"ingot-{metalCode}");

    Item? item = Api.World.GetItem(loc);
    if (item == null)
      return null;

    var stack = new ItemStack(item, units);
    item.SetTemperature(Api.World, stack, temp, false);
    return stack;
  }

  #endregion

  #region Tap draining

  private void DrainIronTap(ref bool dirty) {
    BlockPos lowerTapPos = GetGlobalPos(2, -2, 2);
    if (
      Api.World.BlockAccessor.GetBlockEntity(lowerTapPos)
        is not BlockEntityBlastFurnaceTap lowerTap
      || !lowerTap.IsPouring
      || _moltenIron <= 0
    )
      return;

    int units = Math.Min(SmexValues.BfIronTapDrainPerTick, (int)_moltenIron);
    ItemStack? ironStack = CreateMoltenStack("iron", units, _internalTemp);
    if (ironStack == null)
      return;

    int accepted = lowerTap.TryPourMetal(ironStack, _internalTemp);
    if (accepted > 0) {
      _moltenIron -= accepted;
      dirty = true;
      ExSounds.PlayThrottled(
        Api,
        lowerTapPos,
        ExSounds.MoltenMetal,
        ref _lastTapSoundMs,
        2000,
        0.5f
      );
    }
  }

  private void DrainSlagTap(ref bool dirty) {
    BlockPos higherTapPos = GetGlobalPos(-2, -1, 2);
    if (
      Api.World.BlockAccessor.GetBlockEntity(higherTapPos)
        is not BlockEntityBlastFurnaceTap higherTap
      || !higherTap.IsPouring
      || _moltenSlag <= 0
    )
      return;

    int units = Math.Min(SmexValues.BfSlagTapDrainPerTick, (int)_moltenSlag);
    ItemStack? slagStack = CreateMoltenStack("slag", units, _internalTemp);
    if (slagStack == null)
      return;

    int accepted = higherTap.TryPourMetal(slagStack, _internalTemp);
    if (accepted > 0) {
      _moltenSlag -= accepted;
      dirty = true;
      ExSounds.PlayThrottled(
        Api,
        higherTapPos,
        ExSounds.MoltenMetal,
        ref _lastTapSoundMs,
        2000,
        0.5f
      );
    }
  }

  #endregion

  #region Tick

  private void ScanForOutlets() {
    _gasOutlets = [GetGlobalPos(0, 3, 1), GetGlobalPos(0, 3, 3)];
    _tuyeres = [GetGlobalPos(0, -2, 1), GetGlobalPos(0, -2, 3)];
  }

  protected override void OnProductionTick(float dt) {
    if (!StructureComplete)
      return;

    // Re-read the tunables each tick so a live `/exmod config smex ...` change (e.g. shortening the
    // melt-start delay) takes effect immediately, instead of staying pinned to whatever was cached
    // when this furnace last loaded. Reading the generated accessor is a plain field read - cheap.
    CacheAttributes();

    bool dirty = false;

    bool anyOutlet = false;
    bool anyAccepted = false;
    if (State != BlastFurnaceState.Idle) {
      // Flue volume tracks the blast: what is blown in comes back out. Uses the previous tick's
      // draw - the tuyeres are metered further down.
      float exhaustVolume = Math.Max(
        SmexValues.BfExhaustBaseVolume,
        _airDrawn * SmexValues.BfExhaustPerAirDrawn
      );
      foreach (var pos in _gasOutlets) {
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is IPipeNode outlet) {
          anyOutlet = true;
          if (
            outlet.TryProduce(
              exhaustVolume,
              _internalTemp * SmexValues.BfExhaustTempFraction,
              "Exhaust",
              maxOutputPressure: SmexValues.BfExhaustOutputPressure
            )
          )
            anyAccepted = true;
        } else {
          ScanForOutlets();
        }
      }
    }

    // Choked means the flue has nowhere to go at all. Judging it on ANY outlet refusing made a
    // furnace with one outlet piped and the other left bare permanently choked: the lone one is a
    // single-node network that fills in two ticks and then refuses forever.
    bool choked = anyOutlet && !anyAccepted;
    if (IsChoked != choked) {
      IsChoked = choked;
      dirty = true;
    }

    // One scan of the hearth region per tick; all pile-based reads/writes below
    // reuse this list instead of re-walking the 3×7×3 box.
    var hearthPiles = CollectHearthPiles();
    int mixCount = GetBlastMixCount(hearthPiles, out bool isFull);
    if (_cachedMixCount != mixCount || _cachedIsFull != isFull)
      dirty = true;
    _cachedMixCount = mixCount;
    _cachedIsFull = isFull;

    bool tuyeresReceiveExhaust = false;
    float hotBlastTemp = _ambientTemp;
    bool receivingBlast = false;

    // Air demand follows the melt rate the heat margin supports, so a hotter hearth breathes harder.
    // Taken from the temperature this tick opens with: the draw leads the heating by one tick.
    float heatFactor = HeatFactor(_internalTemp);
    float perTuyereDemand = _tuyereIntakeVolume * heatFactor;
    _airRequested = _tuyeres.Count * perTuyereDemand;
    _airDrawn = 0f;

    foreach (var pos in _tuyeres) {
      if (Api.World.BlockAccessor.GetBlockEntity(pos) is IPipeNode tuyere) {
        // An unlit furnace reads its tuyeres but draws nothing through them.
        if (State != BlastFurnaceState.Idle)
          _airDrawn += tuyere.TryConsume(perTuyereDemand);
        if (tuyere is BlockEntityPipe pipe) {
          if (pipe.Medium == "Exhaust")
            tuyeresReceiveExhaust = true;

          if (
            pipe.Medium == "Air"
            && pipe.Pressure >= SmexValues.BfBlastPressureThreshold
          ) {
            hotBlastTemp = Math.Max(hotBlastTemp, pipe.Temperature);
            receivingBlast = true;
          }
        }
      } else {
        ScanForOutlets();
      }
    }

    bool isDoorOpen = GetBehavior<BEBehaviorDoor>()?.Opened == true;
    bool isLiquidCapacityReached =
      _moltenIron >= _maxMoltenIron || _moltenSlag >= _maxMoltenSlag;

    if (
      State == BlastFurnaceState.Idle
      && StructureComplete
      && _cachedIsFull
      && !IsChoked
      && !isDoorOpen
    ) {
      CheckHearthBurning(hearthPiles, out _, out bool allBurning);
      if (allBurning) {
        State = BlastFurnaceState.Firing;
        _internalTemp = _ignitionTemp;
        dirty = true;
        // Whoosh as the charge catches.
        ExSounds.Play(Api, GetGlobalPos(0, 0, 2), ExSounds.Ignite, 1f, 32f);
      }
    }

    if (State != BlastFurnaceState.Idle) {
      int disruptionCount = 0;
      if (mixCount < _blastMixRequiredToRun)
        disruptionCount++;
      if (tuyeresReceiveExhaust)
        disruptionCount++;
      // A blocked flue and a full reservoir are stalls, not disruptions - they gate the melt below
      // and leave the campaign alive. Swapping the regenerator stoves shuts the exhaust by design.
      if (isDoorOpen)
        disruptionCount++;

      if (disruptionCount > 0) {
        _extinguishSeconds += dt;
        dirty = true;

        float extinguishThreshold = _disruptionGraceSeconds;
        if (disruptionCount >= 2)
          extinguishThreshold = 0f;
        else if (isDoorOpen)
          extinguishThreshold = _doorOpenGraceSeconds;

        if (_extinguishSeconds >= extinguishThreshold) {
          Extinguish();
          return;
        }
      } else {
        if (_extinguishSeconds != 0)
          dirty = true;
        _extinguishSeconds = 0;
      }
    }

    if (State == BlastFurnaceState.Firing || State == BlastFurnaceState.Melting) {
      // Roaring furnace ambience while lit.
      ExSounds.PlayThrottled(
        Api,
        GetGlobalPos(0, 0, 2),
        ExSounds.Fire,
        ref _lastFireSoundMs,
        5000,
        0.6f,
        32f
      );

      // Ceiling and heating rate both interpolate with the blast temperature; cold blast still
      // clears the melting point. A choked flue reads as no blast.
      float blastFraction = BlastFraction(hotBlastTemp);
      _targetTemp =
        _naturalMaxTemp + (_boostedMaxTemp - _naturalMaxTemp) * blastFraction;

      float oldTemp = _internalTemp;
      // Heating/cooling rates are per-second; scale by dt for tick-independence.
      float heatRate = receivingBlast
        ? _heatRateBase + (_heatRateHot - _heatRateBase) * blastFraction
        : _heatRateUnblown;

      if (_internalTemp < _targetTemp)
        _internalTemp = Math.Min(_internalTemp + heatRate * dt, _targetTemp);
      else if (_internalTemp > _targetTemp)
        _internalTemp = Math.Max(_internalTemp - _heatRateBase * dt, _targetTemp);

      _internalTemp = GameMath.Clamp(
        _internalTemp,
        _ambientTemp,
        Math.Max(_naturalMaxTemp, _boostedMaxTemp)
      );
      if (Math.Abs(_internalTemp - oldTemp) > 0.1f)
        dirty = true;

      if (State == BlastFurnaceState.Firing) {
        _meltSpeed = 0f;
        if (_internalTemp >= _ironMeltingPoint) {
          TransitionToMelting();
          return;
        }
      } else if (State == BlastFurnaceState.Melting) {
        if (_internalTemp < _ironMeltingPoint) {
          _meltSpeed = 0f;
          _belowMeltingSeconds += dt;
          dirty = true;
          if (_belowMeltingSeconds >= _meltStallSeconds) {
            State = BlastFurnaceState.Firing;
            _belowMeltingSeconds = 0;
            dirty = true;
          }
        } else {
          if (_belowMeltingSeconds != 0)
            dirty = true;
          _belowMeltingSeconds = 0;

          // Melt rate = the heat margin's rate, scaled by the share of the air it asked for that
          // arrived. Scaling rather than compounding keeps air demand out of the rate that set it.
          float airFactor =
            _airRequested > 0f
              ? GameMath.Clamp(_airDrawn / _airRequested, 0f, 1f)
              : 0f;
          _meltSpeed = heatFactor * airFactor;

          // Halts on the same tick the flue blocks or the reservoir fills.
          if (!isLiquidCapacityReached && !IsChoked) {
            _meltSeconds += _meltSpeed * dt;
            if (_meltSeconds >= _meltIntervalSec) {
              _meltSeconds -= _meltIntervalSec;
              ConsumeForMelting(
                hearthPiles,
                _blastMixPerMeltCycle,
                _ironPerMeltCycle,
                _slagPerMeltCycle
              );
              dirty = true;
            }
          }

          DrainIronTap(ref dirty);
          DrainSlagTap(ref dirty);
        }
      }
    } else {
      _meltSpeed = 0f;
    }

    if (dirty)
      MarkDirty(true);
  }

  #endregion

  #region Heat and melt model

  /// <summary>
  /// How far the blast is toward <see cref="SmexConfig.BfBlastTempReference"/>, 0..1. The hearth
  /// ceiling and the heating rate both interpolate across it. A choked flue reads as no blast.
  /// </summary>
  private float BlastFraction(float blastTemp) =>
    IsChoked || _blastTempReference <= 0f
      ? 0f
      : GameMath.Clamp(blastTemp / _blastTempReference, 0f, 1f);

  /// <summary>
  /// The melt-rate multiplier the hearth's heat margin supports, before the air supply is applied.
  /// Also sets the tuyere draw.
  /// </summary>
  private float HeatFactor(float hearthTemp) =>
    GameMath.Clamp(
      _meltSpeedBase
        + (hearthTemp - _ironMeltingPoint) / 100f * _meltGainPer100C,
      _meltSpeedMin,
      _meltSpeedMax
    );

  #endregion

  #region Private helpers

  /// <summary>
  /// Walks the 3×7×3 hearth region once and returns every coal-pile BE, so all per-tick pile
  /// reads/writes share one walk.
  /// </summary>
  private List<(BlockPos pos, BlockEntityCoalPile pile)> CollectHearthPiles() {
    var piles = new List<(BlockPos, BlockEntityCoalPile)>();
    BlockPos centerHearth = GetGlobalPos(0, 0, 2);
    Api.World.BlockAccessor.WalkBlocks(
      centerHearth.AddCopy(-1, -3, -1),
      centerHearth.AddCopy(1, 3, 1),
      (block, x, y, z) => {
        if (block.Code?.Path.StartsWith("coalpile") != true)
          return;
        BlockPos pos = new(x, y, z, Pos.dimension);
        if (
          Api.World.BlockAccessor.GetBlockEntity(pos)
          is BlockEntityCoalPile pileBe
        )
          piles.Add((pos, pileBe));
      }
    );
    return piles;
  }

  private static void CheckHearthBurning(
    List<(BlockPos pos, BlockEntityCoalPile pile)> piles,
    out bool anyBurning,
    out bool allBurning
  ) {
    bool any = false;
    bool all = true;
    foreach (var (_, pileBe) in piles) {
      if (pileBe.IsBurning)
        any = true;
      else
        all = false;
    }
    anyBurning = any;
    allBurning = all && piles.Count > 0;
  }

  private void TransitionToMelting() {
    State = BlastFurnaceState.Melting;
    _meltSeconds = 0;
    _belowMeltingSeconds = 0;
    MarkDirty(true);
  }

  private void ConsumeForMelting(
    List<(BlockPos pos, BlockEntityCoalPile pile)> piles,
    int blastmixToConsume,
    float ironProduced,
    float slagProduced
  ) {
    int consumed = 0;

    // Consume top-down so upper piles empty first, matching the original drip order.
    piles.Sort((a, b) => b.pos.Y.CompareTo(a.pos.Y));
    foreach (var (pos, pileBe) in piles) {
      if (consumed >= blastmixToConsume)
        break;
      if (pileBe.inventory is not { Count: > 0 })
        continue;

      var slot = pileBe.inventory[0];
      if (slot.Empty || slot.Itemstack.Collectible.Code.Path != "blastmix")
        continue;

      int take = System.Math.Min(slot.StackSize, blastmixToConsume - consumed);
      slot.TakeOut(take);
      slot.MarkDirty();
      pileBe.MarkDirty(true);
      consumed += take;
      if (slot.Empty)
        Api.World.BlockAccessor.SetBlock(0, pos);
    }

    // Yield is proportional to the mix actually consumed. Paying the full cycle for a part charge
    // would let a drip-fed hearth print iron.
    float yieldShare =
      blastmixToConsume > 0 ? (float)consumed / blastmixToConsume : 0f;
    _moltenIron = System.Math.Min(
      _moltenIron + ironProduced * yieldShare,
      _maxMoltenIron
    );
    _moltenSlag = System.Math.Min(
      _moltenSlag + slagProduced * yieldShare,
      _maxMoltenSlag
    );
  }

  private void Extinguish() {
    if (State != BlastFurnaceState.Idle)
      ExSounds.Play(Api, GetGlobalPos(0, 0, 2), ExSounds.Extinguish, 1f, 32f);

    State = BlastFurnaceState.Idle;
    _internalTemp = _ambientTemp;
    _meltSpeed = 0f;
    _airDrawn = 0f;
    _airRequested = 0f;

    if (_moltenIron > 0) {
      Block? solidIronBlock = Api.World.GetBlock(
        new AssetLocation("smex", "solidifiediron")
      );
      if (solidIronBlock != null) {
        int totalNuggets = System.Math.Max(
          1,
          (int)System.Math.Floor(_moltenIron / SmexValues.MoltenUnitsPerBit)
        );
        int nuggets1 =
          totalNuggets < 3 ? 1 : Api.World.Rand.Next(1, totalNuggets - 1);
        int nuggets2 = totalNuggets - nuggets1;

        BlockPos pos1 = GetGlobalPos(0, -2, 2);
        BlockPos pos2 = GetGlobalPos(1, -2, 2);

        Api.World.BlockAccessor.SetBlock(solidIronBlock.BlockId, pos1);
        if (
          Api.World.BlockAccessor.GetBlockEntity(pos1)
          is BlockEntitySolidifiedIron be1
        ) {
          be1.IronCount = nuggets1;
          be1.MarkDirty(true);
        }

        Api.World.BlockAccessor.SetBlock(solidIronBlock.BlockId, pos2);
        if (
          Api.World.BlockAccessor.GetBlockEntity(pos2)
          is BlockEntitySolidifiedIron be2
        ) {
          be2.IronCount = nuggets2;
          be2.MarkDirty(true);
        }
      }
    }

    _moltenIron = 0;
    _moltenSlag = 0;
    _meltSeconds = 0;
    _extinguishSeconds = 0;
    _belowMeltingSeconds = 0;

    // Hand the hearth piles back to their own burn clock and put them out. The mix itself is left
    // as loaded, ready to be lit again.
    BlockPos centerHearth = GetGlobalPos(0, 0, 2);
    Api.World.BlockAccessor.WalkBlocks(
      centerHearth.AddCopy(-1, -3, -1),
      centerHearth.AddCopy(1, 3, 1),
      (block, x, y, z) => {
        if (block.Code?.Path.StartsWith("coalpile") == true) {
          BlockPos pos = new BlockPos(x, y, z, Pos.dimension);
          if (
            Api.World.BlockAccessor.GetBlockEntity(pos)
            is BlockEntityCoalPile pileBe
          )
            BlastmixPiles.Release(pileBe);
        }
      }
    );

    MarkDirty(true);
  }

  private int GetBlastMixCount(
    List<(BlockPos pos, BlockEntityCoalPile pile)> piles,
    out bool isFull
  ) {
    int totalMix = 0;
    foreach (var (_, pileBe) in piles) {
      // While lit, the furnace manages and keeps its hearth piles burning.
      if (State != BlastFurnaceState.Idle) {
        BlastmixPiles.SetManagedByFurnace(pileBe, true);
        if (!pileBe.IsBurning)
          pileBe.TryIgnite();
      }
      foreach (var slot in pileBe.inventory) {
        if (
          !slot.Empty && slot.Itemstack.Collectible.Code.Path.Equals("blastmix")
        )
          totalMix += slot.StackSize;
      }
    }

    isFull = totalMix >= SmexValues.BlastMixRequiredToFire;
    return totalMix;
  }

  #endregion

  #region Block lifecycle

  public override void OnBlockRemoved() {
    if (Api?.Side == EnumAppSide.Server && State != BlastFurnaceState.Idle)
      Extinguish();
    base.OnBlockRemoved();
  }

  #endregion

  #region Serialization

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  ) {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    IsChoked = tree.GetBool("isChoked");
    State = (BlastFurnaceState)tree.GetInt("bfState", 0);
    _internalTemp = tree.GetFloat("internalTemp", 20f);
    _meltSeconds = tree.GetFloat("meltSeconds", 0);
    _extinguishSeconds = tree.GetFloat("extinguishSeconds", 0);
    _belowMeltingSeconds = tree.GetFloat("belowMeltingSeconds", 0);
    _moltenIron = tree.GetFloat("moltenIron", 0f);
    _moltenSlag = tree.GetFloat("moltenSlag", 0f);
    _meltSpeed = tree.GetFloat("meltSpeed", 0f);
    _airDrawn = tree.GetFloat("airDrawn", 0f);
    _airRequested = tree.GetFloat("airRequested", 0f);
    _targetTemp = tree.GetFloat("targetTemp", 20f);
    // "secondsAboveMelting" and "fuelBurnSeconds" are vestigial keys in old saves - not read.
    _cachedMixCount = tree.GetInt("cachedMixCount", 0);
    _cachedIsFull = tree.GetBool("cachedIsFull", false);
    BaseAngleRad = tree.GetFloat("baseAngleRad", -1f);
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("isChoked", IsChoked);
    tree.SetInt("bfState", (int)State);
    tree.SetFloat("internalTemp", _internalTemp);
    tree.SetFloat("meltSeconds", _meltSeconds);
    tree.SetFloat("extinguishSeconds", _extinguishSeconds);
    tree.SetFloat("belowMeltingSeconds", _belowMeltingSeconds);
    tree.SetFloat("moltenIron", _moltenIron);
    tree.SetFloat("moltenSlag", _moltenSlag);
    tree.SetFloat("meltSpeed", _meltSpeed);
    tree.SetFloat("airDrawn", _airDrawn);
    tree.SetFloat("airRequested", _airRequested);
    tree.SetFloat("targetTemp", _targetTemp);
    tree.SetInt("cachedMixCount", _cachedMixCount);
    tree.SetBool("cachedIsFull", _cachedIsFull);
    tree.SetFloat("baseAngleRad", BaseAngleRad);
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    long now = Api.World.ElapsedMilliseconds;
    if (now - _lastInfoUpdate > 1000) {
      StringBuilder sb = new StringBuilder();
      if (!StructureComplete) {
        sb.AppendLine(Lang.Get("smex:bf-info-incomplete"));
      } else {
        sb.AppendLine(
          Lang.Get(
            "smex:bf-info-mixloaded",
            _cachedMixCount,
            SmexValues.BlastMixRequiredToFire
          )
        );

        if (State != BlastFurnaceState.Idle) {
          string stateName = Lang.Get(
            "smex:bf-state-" + State.ToString().ToLowerInvariant()
          );
          sb.AppendLine(Lang.Get("smex:bf-info-state", stateName));
          // The ceiling sits next to the temperature: a hearth resting at it is at its limit for
          // the blast it is getting, not stuck.
          sb.AppendLine(
            Lang.Get(
              "smex:bf-info-temp",
              ExMeasure.Temperature(_internalTemp),
              ExMeasure.Temperature(_targetTemp)
            )
          );

          // Drawn short of requested is the diagnosis for a slow melt.
          if (_airRequested > 0f)
            sb.AppendLine(
              Lang.Get(
                "smex:bf-info-air",
                ExMeasure.FlowRate(_airDrawn),
                ExMeasure.FlowRate(_airRequested)
              )
            );

          if (State == BlastFurnaceState.Melting) {
            sb.AppendLine(
              Lang.Get("smex:bf-info-meltrate", _meltSpeed.ToString("0.0#"))
            );
            sb.AppendLine(
              Lang.Get("smex:bf-info-molteniron", _moltenIron, _maxMoltenIron)
            );
            sb.AppendLine(
              Lang.Get("smex:bf-info-moltenslag", _moltenSlag, _maxMoltenSlag)
            );
            // A full reservoir stalls the melt silently; tapping either vessel resumes it.
            if (
              _moltenIron >= _maxMoltenIron
              || _moltenSlag >= _maxMoltenSlag
            )
              sb.AppendLine(Lang.Get("smex:bf-info-reservoirfull"));
          }

          // The choke stalls a lit furnace, so its reason belongs in this branch: IsChoked is
          // never set by the time the Idle branch below runs, which would leave the player
          // watching a stopped furnace with no explanation.
          if (IsChoked)
            sb.AppendLine(Lang.Get("smex:bf-info-exhaustfull"));

          if (_extinguishSeconds > 0) {
            float maxExtinguish =
              (GetBehavior<BEBehaviorDoor>()?.Opened == true)
                ? _doorOpenGraceSeconds
                : _disruptionGraceSeconds;
            int remainingSeconds = (int)
              System.Math.Max(0f, maxExtinguish - _extinguishSeconds);
            sb.AppendLine(
              Lang.Get("smex:bf-info-extinguishingin", remainingSeconds)
            );
          }
        } else {
          if (IsChoked)
            sb.AppendLine(Lang.Get("smex:bf-info-exhaustfull"));
          else if (GetBehavior<BEBehaviorDoor>()?.Opened == true)
            sb.AppendLine(Lang.Get("smex:bf-info-doorclosed"));
          else if (!_cachedIsFull)
            sb.AppendLine(Lang.Get("smex:bf-info-needsmix"));
          else {
            CheckHearthBurning(
              CollectHearthPiles(),
              out bool anyBurning,
              out bool allBurning
            );
            sb.AppendLine(
              Lang.Get(
                anyBurning && !allBurning
                  ? "smex:bf-info-partiallylit"
                  : "smex:bf-info-ready"
              )
            );
          }
        }
      }
      _cachedInfoText = sb.ToString();
      _lastInfoUpdate = now;
    }
    dsc.Append(_cachedInfoText);
  }

  #endregion
}

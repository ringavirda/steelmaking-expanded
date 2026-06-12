using System;
using System.Collections.Generic;
using System.Text;
using ExpandedLib;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using SteelmakingExpanded.Overrides;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>Operating state of the blast furnace.</summary>
public enum BlastFurnaceState
{
  /// <summary>Not lit.</summary>
  Idle,

  /// <summary>Lit and heating up, but not yet hot enough to melt iron.</summary>
  Firing,

  /// <summary>Hot enough to melt iron; producing molten iron and slag.</summary>
  Melting,
}

/// <summary>
/// Block entity for the blast furnace multiblock. Drives the firing/melting state
/// machine: consumes blast mix, draws air/blast through the tuyeres, vents exhaust
/// through the gas outlets, accumulates molten iron and slag, and feeds the taps.
/// </summary>
[EntityRegister]
public class BlockEntityBlastFurnace : BlockEntityMultiblockStructure
{
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
  private float _secondsAboveMelting = 0;
  private float _meltSeconds = 0;
  private float _extinguishSeconds = 0;
  private float _belowMeltingSeconds = 0;
  private float _moltenIron = 0;
  private float _moltenSlag = 0;
  private float _fuelBurnSeconds = 0;

  /// <summary>Base yaw (radians) of the furnace door, used to orient the multiblock structure.</summary>
  public float BaseAngleRad { get; set; } = -1f;

  // Sound throttles (world-elapsed ms): the furnace fire ambience and the molten
  // tap-pour hiss are looping, gated so the per-second tick doesn't spam audio.
  private long _lastFireSoundMs;
  private long _lastTapSoundMs;

  private string _cachedInfoText = "";
  private long _lastInfoUpdate = 0;

  // Cached block attributes (constant per block type) — read once at init instead
  // of re-parsing the JsonObject every production tick / HUD refresh.
  private float _naturalMaxTemp;
  private float _boostedMaxTemp;
  private float _blastBoostThreshold;
  private float _ironMeltingPoint;
  private int _maxFuelBurnTime;
  private float _meltStartDelay;
  private float _meltIntervalSec;
  private float _ironPerMeltCycle;
  private float _slagPerMeltCycle;
  private int _blastMixPerMeltCycle;
  private float _maxMoltenIron;
  private float _maxMoltenSlag;

  protected override int CompletionTickMs => 3000;

  #region Abstract method implementations

  protected override void UpdateStructureRotation()
  {
    if (Block == null)
      return;

    if (BaseAngleRad < 0)
    {
      var doorBehavior = GetBehavior<BEBehaviorDoor>();
      BaseAngleRad = doorBehavior != null ? doorBehavior.RotateYRad : 0;
    }

    float angleDeg = BaseAngleRad * GameMath.RAD2DEG % 360;
    if (angleDeg < 0)
      angleDeg += 360;
    int snappedAngle = (int)System.Math.Round(angleDeg / 90.0) * 90 % 360;
    if (snappedAngle < 0)
      snappedAngle += 360;

    int rotated = snappedAngle % 360;

    if (_structure == null || _currentAngle != rotated)
    {
      _structure = Block.Attributes?[
        "multiblockStructure"
      ]?.AsObject<MultiblockStructure>();
      _structure?.InitForUse(snappedAngle);
      _currentAngle = rotated;

      if (Api is ICoreClientAPI capi && _highlightedStructure != null)
      {
        _highlightedStructure.ClearHighlights(Api.World, capi.World.Player);
        _highlightedStructure = null;
      }
    }
  }

  protected override void OnStructureCompleted() => ScanForOutlets();

  protected override void OnStructureLost()
  {
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

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    CacheAttributes();
    if (api.Side == EnumAppSide.Server && StructureComplete)
      ScanForOutlets();
  }

  private void CacheAttributes()
  {
    _naturalMaxTemp = SmexValues.BfNaturalMaxTemp;
    _boostedMaxTemp = SmexValues.BfBoostedMaxTemp;
    _blastBoostThreshold = SmexValues.BfBlastBoostThreshold;
    _ironMeltingPoint = SmexValues.BfIronMeltingPoint;
    _maxFuelBurnTime = SmexValues.BfMaxFuelBurnTime;
    _meltStartDelay = SmexValues.BfMeltStartDelay;
    _meltIntervalSec = SmexValues.BfMeltIntervalSec;
    _ironPerMeltCycle = SmexValues.BfIronPerMeltCycle;
    _slagPerMeltCycle = SmexValues.BfSlagPerMeltCycle;
    _blastMixPerMeltCycle = SmexValues.BfBlastMixPerMeltCycle;
    _maxMoltenIron = SmexValues.BfMaxMoltenIron;
    _maxMoltenSlag = SmexValues.BfMaxMoltenSlag;
  }

  #endregion

  #region Molten stack construction

  private ItemStack? CreateMoltenStack(string metalCode, int units, float temp)
  {
    // Use the same item codes the molten network/molds expect downstream:
    // iron flows as game:ingot-iron (LastCodePart "iron" drives the tool-mold
    // {metal} drop), slag as smex:slag. The previously used "metal-liquid-*"
    // items do not exist, so GetItem returned null and nothing was ever tapped.
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

  private void DrainIronTap(ref bool dirty)
  {
    BlockPos lowerTapPos = GetGlobalPos(2, -2, 2);
    if (
      Api.World.BlockAccessor.GetBlockEntity(lowerTapPos)
        is not BlockEntityBlastFurnaceTap lowerTap
      || !lowerTap.IsPouring
      || _moltenIron <= 0
    )
      return;

    int units = Math.Min(20, (int)_moltenIron);
    ItemStack? ironStack = CreateMoltenStack(
      "iron",
      (int)Math.Ceiling(units * 0.6f),
      _internalTemp
    );
    if (ironStack == null)
      return;

    int accepted = lowerTap.TryPourMetal(ironStack, _internalTemp);
    if (accepted > 0)
    {
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

  private void DrainSlagTap(ref bool dirty)
  {
    BlockPos higherTapPos = GetGlobalPos(-2, -1, 2);
    if (
      Api.World.BlockAccessor.GetBlockEntity(higherTapPos)
        is not BlockEntityBlastFurnaceTap higherTap
      || !higherTap.IsPouring
      || _moltenSlag <= 0
    )
      return;

    int units = Math.Min(20, (int)_moltenSlag);
    ItemStack? slagStack = CreateMoltenStack(
      "slag",
      (int)Math.Ceiling(units * 0.8),
      _internalTemp
    );
    if (slagStack == null)
      return;

    int accepted = higherTap.TryPourMetal(slagStack, _internalTemp);
    if (accepted > 0)
    {
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

  private void ScanForOutlets()
  {
    _gasOutlets = [GetGlobalPos(0, 3, 1), GetGlobalPos(0, 3, 3)];
    _tuyeres = [GetGlobalPos(0, -2, 1), GetGlobalPos(0, -2, 3)];
  }

  protected override void OnProductionTick(float dt)
  {
    if (!StructureComplete)
      return;

    bool dirty = false;

    // --- Gas outlets ---
    bool failedAny = false;
    if (State != BlastFurnaceState.Idle)
    {
      foreach (var pos in _gasOutlets)
      {
        if (Api.World.BlockAccessor.GetBlockEntity(pos) is IPipeProducer outlet)
        {
          if (!outlet.TryProduce(24f, _internalTemp * 0.8f, "Exhaust"))
            failedAny = true;
        }
        else
        {
          ScanForOutlets();
        }
      }
    }

    if (IsChoked != failedAny)
    {
      IsChoked = failedAny;
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

    // --- Tuyeres ---
    bool tuyeresReceiveExhaust = false;
    float hotBlastTemp = 20f;
    bool receivingBlast = false;

    foreach (var pos in _tuyeres)
    {
      if (Api.World.BlockAccessor.GetBlockEntity(pos) is IPipeConsumer tuyere)
      {
        float consumed = tuyere.TryConsume(12f);
        if (tuyere is BlockEntityPipe pipe)
        {
          if (pipe.Medium == "Exhaust")
            tuyeresReceiveExhaust = true;

          if (
            pipe.Medium == "Air"
            && pipe.Pressure >= SmexValues.BlastPressureThreshold
          )
          {
            hotBlastTemp = Math.Max(hotBlastTemp, pipe.NetworkTemperature);
            receivingBlast = true;
          }
        }
      }
      else
      {
        ScanForOutlets();
      }
    }

    bool isDoorOpen = GetBehavior<BEBehaviorDoor>()?.Opened == true;
    bool isLiquidCapacityReached =
      _moltenIron >= _maxMoltenIron || _moltenSlag >= _maxMoltenSlag;

    // --- Ignition ---
    if (
      State == BlastFurnaceState.Idle
      && StructureComplete
      && _cachedIsFull
      && !IsChoked
      && !isDoorOpen
    )
    {
      CheckHearthBurning(hearthPiles, out _, out bool allBurning);
      if (allBurning)
      {
        State = BlastFurnaceState.Firing;
        _fuelBurnSeconds = 0;
        _internalTemp = 900f;
        dirty = true;
        // Whoosh as the charge catches.
        ExSounds.Play(Api, GetGlobalPos(0, 0, 2), ExSounds.Ignite, 1f, 32f);
      }
    }

    // --- Disruption / extinguish check ---
    if (State != BlastFurnaceState.Idle)
    {
      int disruptionCount = 0;
      if (mixCount < 144)
        disruptionCount++;
      if (tuyeresReceiveExhaust)
        disruptionCount++;
      if (IsChoked)
        disruptionCount++;
      if (isDoorOpen)
        disruptionCount++;
      if (isLiquidCapacityReached)
        disruptionCount++;

      if (disruptionCount > 0)
      {
        _extinguishSeconds += dt;
        dirty = true;

        int extinguishThreshold = 30;
        if (disruptionCount >= 2)
          extinguishThreshold = 0;
        else if (isDoorOpen)
          extinguishThreshold = 10;

        if (_extinguishSeconds >= extinguishThreshold)
        {
          Extinguish();
          return;
        }
      }
      else
      {
        if (_extinguishSeconds != 0)
          dirty = true;
        _extinguishSeconds = 0;
      }
    }

    // --- Temperature and production ---
    if (State == BlastFurnaceState.Firing || State == BlastFurnaceState.Melting)
    {
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

      float targetTemp =
        hotBlastTemp >= _blastBoostThreshold
          ? _boostedMaxTemp
          : _naturalMaxTemp;
      float oldTemp = _internalTemp;
      // Heating/cooling rates are per-second; scale by dt for tick-independence.
      float heatRate = receivingBlast ? 4f : 2f;

      if (_internalTemp < targetTemp)
        _internalTemp = System.Math.Min(
          _internalTemp + heatRate * dt,
          targetTemp
        );
      else if (_internalTemp > targetTemp)
        _internalTemp = System.Math.Max(_internalTemp - 4f * dt, targetTemp);

      _internalTemp = GameMath.Clamp(_internalTemp, 20f, 1700f);
      if (System.Math.Abs(_internalTemp - oldTemp) > 0.1f)
        dirty = true;

      if (State == BlastFurnaceState.Firing)
      {
        _fuelBurnSeconds += dt;
        if (_fuelBurnSeconds >= _maxFuelBurnTime)
        {
          Extinguish();
          return;
        }

        if (_internalTemp >= _ironMeltingPoint)
        {
          _secondsAboveMelting += dt;
          dirty = true;
          if (_secondsAboveMelting >= _meltStartDelay)
          {
            TransitionToMelting();
            return;
          }
        }
        else
        {
          if (_secondsAboveMelting != 0)
            dirty = true;
          _secondsAboveMelting = 0;
        }
      }
      else if (State == BlastFurnaceState.Melting)
      {
        if (_internalTemp < _ironMeltingPoint)
        {
          _belowMeltingSeconds += dt;
          dirty = true;
          if (_belowMeltingSeconds >= 30)
          {
            State = BlastFurnaceState.Firing;
            _secondsAboveMelting = 0;
            _belowMeltingSeconds = 0;
            _fuelBurnSeconds = 0;
            dirty = true;
          }
        }
        else
        {
          if (_belowMeltingSeconds != 0)
            dirty = true;
          _belowMeltingSeconds = 0;

          if (!isLiquidCapacityReached)
          {
            _meltSeconds += dt;
            if (_meltSeconds >= _meltIntervalSec)
            {
              _meltSeconds = 0;
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
    }

    if (dirty)
      MarkDirty(true);
  }

  #endregion

  #region Private helpers

  /// <summary>
  /// Walks the 3×7×3 hearth region once and returns every coal-pile block entity
  /// found. All per-tick pile reads/writes share this list so the region is walked
  /// at most once per production tick.
  /// </summary>
  private List<(
    BlockPos pos,
    CustomBlockEntityCoalPile pile
  )> CollectHearthPiles()
  {
    var piles = new List<(BlockPos, CustomBlockEntityCoalPile)>();
    BlockPos centerHearth = GetGlobalPos(0, 0, 2);
    Api.World.BlockAccessor.WalkBlocks(
      centerHearth.AddCopy(-1, -3, -1),
      centerHearth.AddCopy(1, 3, 1),
      (block, x, y, z) =>
      {
        if (block.Code?.Path.StartsWith("coalpile") != true)
          return;
        BlockPos pos = new(x, y, z, Pos.dimension);
        if (
          Api.World.BlockAccessor.GetBlockEntity(pos)
          is CustomBlockEntityCoalPile pileBe
        )
          piles.Add((pos, pileBe));
      }
    );
    return piles;
  }

  private static void CheckHearthBurning(
    List<(BlockPos pos, CustomBlockEntityCoalPile pile)> piles,
    out bool anyBurning,
    out bool allBurning
  )
  {
    bool any = false;
    bool all = true;
    foreach (var (_, pileBe) in piles)
    {
      if (pileBe.IsBurning)
        any = true;
      else
        all = false;
    }
    anyBurning = any;
    allBurning = all && piles.Count > 0;
  }

  private void TransitionToMelting()
  {
    State = BlastFurnaceState.Melting;
    _meltSeconds = 0;
    _fuelBurnSeconds = 0;
    MarkDirty(true);
  }

  private void ConsumeForMelting(
    List<(BlockPos pos, CustomBlockEntityCoalPile pile)> piles,
    int blastmixToConsume,
    float ironProduced,
    float slagProduced
  )
  {
    int consumed = 0;

    // Consume top-down so upper piles empty first, matching the original drip order.
    piles.Sort((a, b) => b.pos.Y.CompareTo(a.pos.Y));
    foreach (var (pos, pileBe) in piles)
    {
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

    _moltenIron = System.Math.Min(_moltenIron + ironProduced, _maxMoltenIron);
    _moltenSlag = System.Math.Min(_moltenSlag + slagProduced, _maxMoltenSlag);
  }

  private void Extinguish()
  {
    if (State != BlastFurnaceState.Idle)
      ExSounds.Play(Api, GetGlobalPos(0, 0, 2), ExSounds.Extinguish, 1f, 32f);

    State = BlastFurnaceState.Idle;
    _internalTemp = 20f;

    if (_moltenIron > 0)
    {
      Block? solidIronBlock = Api.World.GetBlock(
        new AssetLocation("smex", "solidifiediron")
      );
      if (solidIronBlock != null)
      {
        int totalNuggets = System.Math.Max(
          1,
          (int)System.Math.Floor(_moltenIron / 5f)
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
        )
        {
          be1.IronCount = nuggets1;
          be1.MarkDirty(true);
        }

        Api.World.BlockAccessor.SetBlock(solidIronBlock.BlockId, pos2);
        if (
          Api.World.BlockAccessor.GetBlockEntity(pos2)
          is BlockEntitySolidifiedIron be2
        )
        {
          be2.IronCount = nuggets2;
          be2.MarkDirty(true);
        }
      }
    }

    _moltenIron = 0;
    _moltenSlag = 0;
    _secondsAboveMelting = 0;
    _meltSeconds = 0;
    _extinguishSeconds = 0;
    _belowMeltingSeconds = 0;
    _fuelBurnSeconds = 0;

    BlockPos centerHearth = GetGlobalPos(0, 0, 2);
    Api.World.BlockAccessor.WalkBlocks(
      centerHearth.AddCopy(-1, -3, -1),
      centerHearth.AddCopy(1, 3, 1),
      (block, x, y, z) =>
      {
        if (block.Code?.Path.StartsWith("coalpile") == true)
        {
          BlockPos pos = new BlockPos(x, y, z, Pos.dimension);
          if (
            Api.World.BlockAccessor.GetBlockEntity(pos)
            is CustomBlockEntityCoalPile pileBe
          )
          {
            pileBe.IsManagedByFurnace = false;
            pileBe.ConvertToSlag();
          }
        }
      }
    );

    MarkDirty(true);
  }

  private int GetBlastMixCount(
    List<(BlockPos pos, CustomBlockEntityCoalPile pile)> piles,
    out bool isFull
  )
  {
    int totalMix = 0;
    foreach (var (_, pileBe) in piles)
    {
      // While lit, the furnace manages and keeps its hearth piles burning.
      if (State != BlastFurnaceState.Idle)
      {
        pileBe.IsManagedByFurnace = true;
        if (!pileBe.IsBurning)
          pileBe.TryIgnite();
      }
      foreach (var slot in pileBe.inventory)
      {
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

  public override void OnBlockRemoved()
  {
    if (Api?.Side == EnumAppSide.Server && State != BlastFurnaceState.Idle)
      Extinguish();
    base.OnBlockRemoved();
  }

  #endregion

  #region Serialization

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldAccessForResolve
  )
  {
    base.FromTreeAttributes(tree, worldAccessForResolve);
    IsChoked = tree.GetBool("isChoked");
    State = (BlastFurnaceState)tree.GetInt("bfState", 0);
    _internalTemp = tree.GetFloat("internalTemp", 20f);
    _secondsAboveMelting = tree.GetFloat("secondsAboveMelting", 0);
    _meltSeconds = tree.GetFloat("meltSeconds", 0);
    _extinguishSeconds = tree.GetFloat("extinguishSeconds", 0);
    _belowMeltingSeconds = tree.GetFloat("belowMeltingSeconds", 0);
    _moltenIron = tree.GetFloat("moltenIron", 0f);
    _moltenSlag = tree.GetFloat("moltenSlag", 0f);
    _fuelBurnSeconds = tree.GetFloat("fuelBurnSeconds", 0);
    _cachedMixCount = tree.GetInt("cachedMixCount", 0);
    _cachedIsFull = tree.GetBool("cachedIsFull", false);
    BaseAngleRad = tree.GetFloat("baseAngleRad", -1f);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("isChoked", IsChoked);
    tree.SetInt("bfState", (int)State);
    tree.SetFloat("internalTemp", _internalTemp);
    tree.SetFloat("secondsAboveMelting", _secondsAboveMelting);
    tree.SetFloat("meltSeconds", _meltSeconds);
    tree.SetFloat("extinguishSeconds", _extinguishSeconds);
    tree.SetFloat("belowMeltingSeconds", _belowMeltingSeconds);
    tree.SetFloat("moltenIron", _moltenIron);
    tree.SetFloat("moltenSlag", _moltenSlag);
    tree.SetFloat("fuelBurnSeconds", _fuelBurnSeconds);
    tree.SetInt("cachedMixCount", _cachedMixCount);
    tree.SetBool("cachedIsFull", _cachedIsFull);
    tree.SetFloat("baseAngleRad", BaseAngleRad);
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    long now = Api.World.ElapsedMilliseconds;
    if (now - _lastInfoUpdate > 1000)
    {
      StringBuilder sb = new StringBuilder();
      if (!StructureComplete)
      {
        sb.AppendLine(Lang.Get("smex:bf-info-incomplete"));
      }
      else
      {
        sb.AppendLine(
          Lang.Get(
            "smex:bf-info-mixloaded",
            _cachedMixCount,
            SmexValues.BlastMixRequiredToFire
          )
        );

        if (State != BlastFurnaceState.Idle)
        {
          string stateName = Lang.Get(
            "smex:bf-state-" + State.ToString().ToLowerInvariant()
          );
          sb.AppendLine(Lang.Get("smex:bf-info-state", stateName));
          sb.AppendLine(Lang.Get("smex:bf-info-temp", _internalTemp));

          if (State == BlastFurnaceState.Melting)
          {
            sb.AppendLine(
              Lang.Get("smex:bf-info-molteniron", _moltenIron, _maxMoltenIron)
            );
            sb.AppendLine(
              Lang.Get("smex:bf-info-moltenslag", _moltenSlag, _maxMoltenSlag)
            );
          }
          else if (
            State == BlastFurnaceState.Firing
            && _internalTemp >= _ironMeltingPoint
          )
          {
            // Progress toward the Melting phase as a percentage (matches the
            // Bessemer converter's readout) rather than a raw seconds countdown.
            int pct = (int)
              GameMath.Clamp(
                100f
                  * _secondsAboveMelting
                  / System.Math.Max(1f, _meltStartDelay),
                0,
                100
              );
            sb.AppendLine(Lang.Get("smex:bf-info-meltingin", pct));
          }

          if (_extinguishSeconds > 0)
          {
            int maxExtinguish =
              (GetBehavior<BEBehaviorDoor>()?.Opened == true) ? 10 : 30;
            int remainingSeconds = (int)
              System.Math.Max(0f, maxExtinguish - _extinguishSeconds);
            sb.AppendLine(
              Lang.Get("smex:bf-info-extinguishingin", remainingSeconds)
            );
          }
        }
        else
        {
          if (IsChoked)
            sb.AppendLine(Lang.Get("smex:bf-info-exhaustfull"));
          else if (GetBehavior<BEBehaviorDoor>()?.Opened == true)
            sb.AppendLine(Lang.Get("smex:bf-info-doorclosed"));
          else if (!_cachedIsFull)
            sb.AppendLine(Lang.Get("smex:bf-info-needsmix"));
          else
          {
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

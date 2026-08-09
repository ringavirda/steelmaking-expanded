using System;
using System.Text;
using ExpandedLib.Blocks.Machines;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.Helpers;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Vintagestory.GameContent.Mechanics;

namespace SteelmakingExpanded.BlockStructures.Converter.BlockEntities;

/// <summary>
/// The "brain" of the Bessemer converter multiblock. Owns the operational state
/// machine, the molten charge, and the iron→steel refining process. The
/// converter block is a thin visual/break shell driven from here; the control
/// reads its peripherals (input tap, gas intake, output start, transmission) by
/// resolving their structure-local offsets through <see cref="GetGlobalPos"/>.
/// </summary>
[BlockEntityRegister]
public class BlockEntityConverterControl : BlockEntityMultiblockStructure {
  #region Structure-local peripheral offsets
  private static readonly (int x, int y, int z) TransmissionLocal = (0, -1, 0);
  private static readonly (int x, int y, int z) ConverterLocal = (0, 0, 2);
  private static readonly (int x, int y, int z) GasIntakeLocal = (0, 0, 4);
  private static readonly (int x, int y, int z) InputTapLocal = (1, 1, 2);
  private static readonly (int x, int y, int z) OutputStartLocal = (1, -2, 2);
  #endregion

  #region Tunables (see SmexValues)
  private static int CapacityUnits => SmexValues.BessemerConverterCapacity;
  private static float BlastPerSecond => SmexValues.BessemerBlastPerSecond;
  private static float ProcessDurationSec => SmexValues.BessemerProcessDuration;
  private static float PowerSpeedThreshold =>
    SmexValues.BessemerPowerSpeedThreshold;
  private static int ScrapUnitValue => SmexValues.MoltenUnitsPerBit;

  // The charge cools slower inside the insulated vessel than loose molten metal does: scale the
  // molten-system cooldown speed by the configurable coefficient (0.5 ⇒ cools twice as slowly), so
  // a finished heat gives the player time to pour before it solidifies.
  private static float ContentCooldownSpeed =>
    SmexValues.MoltenCooldownSpeed * SmexValues.BessemerCooldownCoefficient;

  // Below this fraction of capacity a hardened residue is small enough to chisel out (rather than
  // breaking the whole converter to salvage it).
  private static float ChiselMaxFraction =>
    SmexValues.BessemerChiselMaxFraction;

  private const string IronCode = "game:ingot-iron";
  private const string SteelCode = "game:ingot-steel";
  #endregion

  #region Operational + charge state
  /// <summary>Current player-selected operating mode of the converter.</summary>
  public ConverterOpState OpState { get; private set; } =
    ConverterOpState.Normal;

  private ItemStack? _content;
  private int _contentUnits;

  /// <summary>Cold scrap charged into the vessel, in molten units. Counts against capacity
  /// alongside the molten charge and melts in when the blow completes.</summary>
  private int _scrapUnits;

  private float _processSeconds;
  private bool _solidified;
  private string _status = Lang.Get("smex:bessemer-status-idle");

  // Last tick's operating point. Synced, since the bath is never blown client-side.
  private float _processTemp;
  private float _convSpeed;

  private BEBehaviorAnimatable? _animatable;

  // Sound throttles (world-elapsed ms) for the looping ambience. Filling and pouring are
  // mutually exclusive, so they share _lastMoltenSoundMs.
  private long _lastProcessSoundMs;
  private long _lastFireSoundMs;
  private long _lastMoltenSoundMs;
  #endregion

  #region Lifecycle

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();

    // Establish the structure angle up front so GetGlobalPos resolves peripherals on the first
    // production tick (which can fire before the slower completion tick).
    UpdateStructureRotation();

    if (api is ICoreClientAPI capi && _animatable != null) {
      Shape? shape = capi
        .Assets.TryGet(
          Block
            .Shape.Base.Clone()
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json")
        )
        ?.ToObject<Shape>();
      if (shape != null)
        // Rotation is applied only by the renderer, not baked into the mesh (InitializeShapeAnd-
        // Animator does both and would rotate the control 180° off).
        _animatable.animUtil.InitializeAnimator(
          "bessemercontrol-" + Block.Variant["side"],
          shape,
          capi.Tesselator.GetTextureSource(Block),
          new Vec3f(0, Block.Shape.rotateY, 0)
        );
      ApplyControlPose();
    }
  }

  #endregion

  #region Production tick (server only, started by base when StructureComplete)

  protected override void OnProductionTick(float dt) {
    if (!StructureComplete || !IsConverterConstructed()) {
      SetStatus(Lang.Get("smex:bessemer-status-notbuilt"));
      return;
    }

    if (!IsGasIntakeAligned()) {
      SetStatus(Lang.Get("smex:bessemer-status-misaligned"));
      return;
    }

    if (!IsTransmissionAligned()) {
      SetStatus(Lang.Get("smex:bessemer-status-transmission-misaligned"));
      return;
    }

    UpdateSolidified();
    SyncContentCooldown();

    switch (OpState) {
      case ConverterOpState.Filling:
        TickFilling(dt);
        break;
      case ConverterOpState.Pouring:
        TickPouring(dt);
        break;
      default:
        TickNormal(dt);
        break;
    }
  }

  private void TickNormal(float dt) {
    // Idle while holding the charge; run the bessemer refining process if the
    // charge is molten iron and the gas intake is fed blast.
    if (_content == null || _contentUnits <= 0) {
      SetStatus(Lang.Get("smex:bessemer-status-empty"));
      return;
    }

    if (_solidified) {
      SetStatus(SolidifiedStatus());
      return;
    }

    // Refining applies only to molten iron: steel just waits to be poured, anything else is
    // foreign. Either way stop here so no blast is drawn and no process particles are emitted.
    if (!IsMoltenIron()) {
      bool isSteel = _content.Collectible.Code.ToString() == SteelCode;
      SetStatus(
        Lang.Get(
          isSteel
            ? "smex:bessemer-status-steelready"
            : "smex:bessemer-status-foreign"
        )
      );
      return;
    }

    // Refining requires blast: harder blast draws more air and converts quicker, in step.
    float pressure = BlastPressure();
    _convSpeed = ConversionSpeed(pressure);
    float demand = BlastPerSecond * _convSpeed * dt;
    float consumed = TryConsumeBlast(demand);
    if (consumed <= 0f) {
      _convSpeed = 0f;
      SetStatus(
        Lang.Get("smex:bessemer-status-refining-paused", FormatProgress())
      );
      return;
    }

    // Blast short of the demand slows the blow in proportion.
    float airFactor =
      demand > 0f ? GameMath.Clamp(consumed / demand, 0f, 1f) : 0f;
    _convSpeed *= airFactor;

    // The bath runs at the balance between the blow's own heat and its losses, cold scrap included
    // - never at a fixed temperature.
    _processTemp = ProcessTemperature(pressure);
    HoldTemperature(_processTemp);

    // Too cold to refine: the blow stalls and the bath keeps tracking the lower equilibrium, on
    // toward the solidify/chisel path.
    if (_processTemp < SmexValues.BessemerRefineTemperature) {
      _convSpeed = 0f;
      SetStatus(
        Lang.Get(
          "smex:bessemer-status-toocold",
          ExMeasure.Temperature(_processTemp),
          ExMeasure.Temperature(SmexValues.BessemerRefineTemperature)
        )
      );
      MarkDirty();
      return;
    }

    // Emit process smoke only while iron is actively refining.
    GetConverter()?.SpawnSmokeParticles();

    // Roaring blast through the molten bath while refining.
    ExSounds.PlayThrottled(
      Api,
      Pos.AddCopy(0, 0, 2),
      ExSounds.Embers,
      ref _lastProcessSoundMs,
      4000,
      0.5f
    );

    // Crackling fire over the blast (the carbon burning off), offset from the embers throttle so
    // the two loops overlap rather than fire in lockstep.
    ExSounds.PlayThrottled(
      Api,
      Pos.AddCopy(0, 0, 2),
      ExSounds.Fire,
      ref _lastFireSoundMs,
      3000,
      1.5f
    );

    _processSeconds += _convSpeed * dt;
    if (_processSeconds >= ProcessDurationSec)
      CompleteRefining();
    else
      SetStatus(Lang.Get("smex:bessemer-status-refining", FormatProgress()));

    MarkDirty();
  }

  private void TickFilling(float dt) {
    if (_solidified) {
      SetStatus(SolidifiedStatus());
      return;
    }

    var inputCell = GetMoltenCell(InputTapLocal);

    // Respect the tap's open/closed state: a closed tap's cell still receives metal from the
    // network, so check it here or the vessel would fill through a shut tap.
    if (inputCell is BlockEntityMoltenCanalTap { IsPouring: false }) {
      SetStatus(Lang.Get("smex:bessemer-status-filling-tapclosed"));
      return;
    }

    if (inputCell == null || !inputCell.HasMoltenMetal) {
      SetStatus(Lang.Get("smex:bessemer-status-filling-nometal"));
      return;
    }

    // Charged scrap takes up room, so it counts against the fill.
    if (_contentUnits + _scrapUnits >= CapacityUnits) {
      SetStatus(Lang.Get("smex:bessemer-status-filling-full"));
      return;
    }

    // Only accept a single metal type at a time.
    if (
      _content != null
      && _content.Collectible.Code.ToString() != inputCell.CellMetalType
    ) {
      SetStatus(Lang.Get("smex:bessemer-status-filling-mismatch"));
      return;
    }

    int space = CapacityUnits - _contentUnits - _scrapUnits;
    int toDrain = Math.Min(inputCell.CellAmount, space);
    if (toDrain <= 0)
      return;

    // Capture metal identity/temperature before draining empties the cell.
    string type = inputCell.CellMetalType;
    float temp = inputCell.CellTemperature;

    float drained = inputCell.DrainMetal(toDrain);
    if (drained <= 0f)
      return;

    _content ??= MoltenMetal.CreateStack(
      Api.World,
      type,
      temp,
      ContentCooldownSpeed
    );
    if (_content == null)
      return;

    MoltenMetal.SetTemperature(Api.World, _content, temp);
    _contentUnits += (int)drained;
    // Molten metal hissing into the vessel.
    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.Sizzle,
      ref _lastMoltenSoundMs,
      1500,
      0.6f
    );
    // A fresh charge of iron restarts the refining clock.
    if (IsMoltenIron())
      _processSeconds = 0f;
    SetStatus(
      Lang.Get("smex:bessemer-status-filling", _contentUnits, CapacityUnits)
    );
    MarkDirty();
  }

  private void TickPouring(float dt) {
    if (_content == null || _contentUnits <= 0) {
      SetStatus(Lang.Get("smex:bessemer-status-pouring-empty"));
      return;
    }

    if (_solidified) {
      SetStatus(SolidifiedStatus());
      return;
    }

    var outputCell = GetMoltenCell(OutputStartLocal);
    if (outputCell == null) {
      SetStatus(Lang.Get("smex:bessemer-status-pouring-nocanal"));
      return;
    }

    int amount = Math.Min(
      _contentUnits,
      Math.Max(1, (int)(CapacityUnits * dt))
    );
    float accepted = outputCell.PushMetal(amount, _content, Api.World);
    if (accepted <= 0f) {
      // Output canal full: keep bathing it in our hot content so it stays molten and keeps
      // feeding downstream instead of cooling to a plug. Mirrors the furnace tap's heat soak.
      outputCell.SoakHeat(
        Api.World,
        _content.Collectible.GetTemperature(Api.World, _content)
      );
      SetStatus(Lang.Get("smex:bessemer-status-pouring-full"));
      return;
    }

    _contentUnits -= (int)accepted;
    // Molten metal pouring out into the output canal.
    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.MoltenMetal,
      ref _lastMoltenSoundMs,
      1500,
      0.6f
    );
    if (_contentUnits <= 0) {
      _contentUnits = 0;
      _content = null;
      _processSeconds = 0f;
      SetStatus(Lang.Get("smex:bessemer-status-emptied"));
    } else {
      SetStatus(
        Lang.Get("smex:bessemer-status-pouring", _contentUnits, CapacityUnits)
      );
    }
    MarkDirty();
  }

  private void CompleteRefining() {
    float temp = MoltenMetal.GetTemperature(Api.World, _content!);
    ItemStack? steelStack = MoltenMetal.CreateStack(
      Api.World,
      SteelCode,
      temp,
      ContentCooldownSpeed
    );
    if (steelStack == null)
      return;
    _content = steelStack;

    // Scrap melts into the finished heat, less what burns off. It joins only here - until the blow
    // ends it is heat load, not metal.
    if (_scrapUnits > 0) {
      _contentUnits += (int)
        System.Math.Round(_scrapUnits * SmexValues.BessemerScrapSteelYield);
      _scrapUnits = 0;
    }

    _processSeconds = ProcessDurationSec;
    SetStatus(Lang.Get("smex:bessemer-status-steelready"));
    MarkDirty();
  }

  #endregion

  #region Temperature handling

  // Re-stamps the live charge's cooldown rate from the (live) config every tick, so an admin changing
  // BessemerCooldownCoefficient (or the base MoltenCooldownSpeed) via /exmod config speeds up or slows
  // down the metal already in the vessel - not just metal added on a later fill. Rebases the cooldown
  // baseline to the current temperature (see MoltenMetal.SyncCooldownSpeed) so the new rate applies from
  // this tick forward even when the charge has been sitting idle (its timestamp would otherwise be stale,
  // and rewriting the rate alone would retro-apply it across that whole idle span).
  private void SyncContentCooldown() {
    if (_content != null)
      MoltenMetal.SyncCooldownSpeed(Api.World, _content, ContentCooldownSpeed);
  }

  /// <summary>
  /// The bath temperature the blow settles at: the blow's own heat, raised by blast pressure, less
  /// radiation and less the cold mass of any charged scrap.
  /// <para>
  /// There is no scrap cap by design - scrap is cold mass on this balance, so the ceiling is
  /// whatever drags the bath under <see cref="SmexConfig.BessemerRefineTemperature"/>.
  /// </para>
  /// </summary>
  private static float ProcessTemperature(float pressure) {
    float overGate = Math.Max(
      0f,
      pressure - SmexValues.BlastPressureThreshold
    );
    return SmexValues.BessemerBaseTemperature
      + overGate * SmexValues.BessemerPressureTempGain
      - SmexValues.BessemerRadiationLoss;
  }

  /// <summary>How fast the blow runs, from the pressure behind it. Scales both the air drawn and
  /// the refining clock.</summary>
  private static float ConversionSpeed(float pressure) {
    float reference = SmexValues.BessemerPressureReference;
    if (reference <= 0f)
      return SmexValues.BessemerSpeedMax;
    float overGate = pressure - SmexValues.BlastPressureThreshold;
    return GameMath.Clamp(
      SmexValues.BessemerSpeedMin
        + overGate
          / reference
          * (SmexValues.BessemerSpeedMax - SmexValues.BessemerSpeedMin),
      SmexValues.BessemerSpeedMin,
      SmexValues.BessemerSpeedMax
    );
  }

  private void HoldTemperature(float target) {
    if (_content == null)
      return;
    MoltenMetal.SetTemperature(
      Api.World,
      _content,
      Math.Max(ExClimate.AmbientAt(this), target)
    );
  }

  private void UpdateSolidified() {
    if (_content == null || _contentUnits <= 0) {
      if (_solidified) {
        _solidified = false;
        SyncConverter();
      }
      return;
    }

    bool nowSolid =
      MoltenMetal.GetTemperature(Api.World, _content)
      < MoltenMetal.MeltingPointOf(Api.World, _content);
    if (nowSolid != _solidified) {
      _solidified = nowSolid;
      if (nowSolid)
        ExSounds.Play(Api, Pos, ExSounds.Extinguish, 0.7f);
      SyncConverter();
      MarkDirty();
    }
  }

  #endregion

  #region Peripheral access
  // The converter's local frame faces opposite the structure angle, so peripherals map at
  // _currentAngle + 180 (see the +180 convention).
  protected override BlockPos GetGlobalPos(
    int localX,
    int localY,
    int localZ
  ) =>
    ExOrientation.GlobalPos(
      Pos,
      localX,
      localY,
      localZ,
      (_currentAngle + 180) % 360
    );

  private BlockPos PeripheralPos((int x, int y, int z) local) =>
    GetGlobalPos(local.x, local.y, local.z);

  private BlockEntityMoltenCanal? GetMoltenCell((int x, int y, int z) local) =>
    Api.World.BlockAccessor.GetBlockEntity(PeripheralPos(local))
    as BlockEntityMoltenCanal;

  /// <summary>
  /// The blast network feeding the intake, or <c>null</c> when there is none or it is not carrying
  /// air at the blast pressure. The intake is a fixed connector, not a node: the network lives in
  /// the cell across its connector face, and only a pipe facing back counts as plumbed in.
  /// </summary>
  private PipeNetwork? BlastNetwork() {
    BlockPos intakePos = PeripheralPos(GasIntakeLocal);
    if (
      Api.World.BlockAccessor.GetBlock(intakePos)
      is not Blocks.BlockConverterIntake intake
    )
      return null;
    if (
      this.NetworkSystem()
        ?.GetConnectedNetworkAcross(
          Api.World.BlockAccessor,
          intakePos,
          intake.ConnectorFace
        )
      is not PipeNetwork pipeNet
    )
      return null;
    return
      pipeNet.State?.MediumType == "Air"
      && pipeNet.State.Pressure >= SmexValues.BlastPressureThreshold
      ? pipeNet
      : null;
  }

  /// <summary>Pressure (atm) at the intake, or 0 when it is not receiving blast.</summary>
  private float BlastPressure() => BlastNetwork()?.State?.Pressure ?? 0f;

  private float TryConsumeBlast(float amount) =>
    BlastNetwork()?.TryConsumeGas(amount, Api.World.BlockAccessor) ?? 0f;

  /// <summary>True if the transmission's mechanical network is turning.</summary>
  public bool HasPower() {
    var be = Api.World.BlockAccessor.GetBlockEntity(
      PeripheralPos(TransmissionLocal)
    );
    if (
      be?.GetBehavior<BEBehaviorMPBase>() is not BEBehaviorMPBase mp
      || mp.Network == null
    )
      return false;
    return Math.Abs(mp.Network.Speed * mp.GearedRatio) > PowerSpeedThreshold;
  }

  private BlockEntityConverterBessemer? GetConverter() =>
    Api.World.BlockAccessor.GetBlockEntity(PeripheralPos(ConverterLocal))
    as BlockEntityConverterBessemer;

  /// <summary>True if the converter vessel block has been placed at its structure offset.</summary>
  public bool IsConverterPresent() =>
    Api.World.BlockAccessor.GetBlock(PeripheralPos(ConverterLocal))
    is Blocks.BlockConverterBessemer;

  /// <summary>True if the converter vessel has finished its right-click construction stages.</summary>
  public bool IsConverterConstructed() =>
    GetConverter()?.IsConstructed ?? false;

  /// <summary>
  /// The gas intake must face the same way as the control (matching <c>side</c> variant) or its
  /// blast connector won't line up. The multiblock check accepts any orientation, so validate here.
  /// </summary>
  public bool IsGasIntakeAligned() {
    Block intake = Api.World.BlockAccessor.GetBlock(
      PeripheralPos(GasIntakeLocal)
    );
    if (intake is not Blocks.BlockConverterIntake)
      return false;

    return intake.Variant["side"] == (Block.Variant["side"] ?? "north");
  }

  /// <summary>
  /// The transmission must face the same way as the control (matching "side" variant) or its axle
  /// connector won't line up. Validated here like the gas intake.
  /// </summary>
  public bool IsTransmissionAligned() {
    Block trans = Api.World.BlockAccessor.GetBlock(
      PeripheralPos(TransmissionLocal)
    );
    if (trans is not Blocks.BlockConverterTransmission)
      return false;

    return trans.Variant["side"] == (Block.Variant["side"] ?? "north");
  }

  #endregion

  #region Content queries

  private bool IsMoltenIron() =>
    _content != null
    && _contentUnits > 0
    && _content.Collectible.Code.ToString() == IronCode
    && MoltenMetal.IsLiquid(Api.World, _content);

  private string FormatProgress() {
    int pct = (int)(100f * _processSeconds / Math.Max(1f, ProcessDurationSec));
    return $"{GameMath.Clamp(pct, 0, 100)}%";
  }

  #endregion

  #region Scrap charging

  /// <summary>True when <paramref name="stack"/> is an item the converter remelts as cold scrap.
  /// Matched against the whole configured list - a one-code test agrees with it only until a second
  /// scrap item exists.</summary>
  private static bool IsScrap(ItemStack? stack) {
    string? code = stack?.Collectible?.Code?.ToString();
    if (code == null)
      return false;
    foreach (
      string entry in SmexValues.BessemerScrapCodes.Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries
          | StringSplitOptions.TrimEntries
      )
    )
      if (new AssetLocation(entry).ToString() == code)
        return true;
    return false;
  }

  /// <summary>
  /// Charges cold scrap from the player's hotbar into the vessel. Scrap is cold mass on the heat
  /// balance until the blow finishes, then melts into the heat.
  /// </summary>
  /// <returns>
  /// <c>false</c> when the held item is not scrap, leaving the click to fall through to the
  /// operating-state selection; <c>true</c> otherwise, with <paramref name="error"/> set when the
  /// charge was refused.
  /// </returns>
  public bool TryChargeScrap(IPlayer byPlayer, out string error) {
    error = "";
    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    if (!IsScrap(held))
      return false;

    if (!CanOperate(out error))
      return true;
    if (_solidified) {
      error = Lang.Get("smex:bessemer-err-scrap-solidified");
      return true;
    }
    // Scrap goes into an empty vessel or a raw iron charge, before the blow - cold lumps dropped
    // into a finished heat would stay unrefined in the steel.
    if (_content != null && !IsMoltenIron()) {
      error = Lang.Get("smex:bessemer-err-scrap-notiron");
      return true;
    }

    int roomBits =
      (CapacityUnits - _contentUnits - _scrapUnits)
      / Math.Max(1, ScrapUnitValue);
    if (roomBits <= 0) {
      error = Lang.Get("smex:bessemer-status-filling-full");
      return true;
    }

    if (Api.Side != EnumAppSide.Server)
      return true;

    int taken = ExInventory.TakeHotbar(byPlayer, IsScrap, roomBits);
    if (taken <= 0)
      return true;

    _scrapUnits += taken * ScrapUnitValue;
    ExSounds.Play(Api, Pos.AddCopy(0, 0, 2), ExSounds.MetalGrinding, 0.6f);
    SetStatus(Lang.Get("smex:bessemer-status-scrap-charged", _scrapUnits));
    MarkDirty(true);
    return true;
  }

  #endregion

  #region Player-driven state transitions

  /// <summary>
  /// Validates that the converter is in a state where the player can change its
  /// operating mode: structure complete, vessel constructed, peripherals aligned,
  /// and mechanical power present. Returns false with a player-facing reason.
  /// </summary>
  public bool CanOperate(out string error) {
    error = "";
    if (!StructureComplete) {
      error = Lang.Get("smex:bessemer-err-incomplete");
      return false;
    }
    if (!IsConverterConstructed()) {
      error = Lang.Get("smex:bessemer-err-notbuilt");
      return false;
    }
    if (!IsGasIntakeAligned()) {
      error = Lang.Get("smex:bessemer-err-intake-misaligned");
      return false;
    }
    if (!IsTransmissionAligned()) {
      error = Lang.Get("smex:bessemer-err-transmission-misaligned");
      return false;
    }
    if (!HasPower()) {
      error = Lang.Get("smex:bessemer-err-nopower");
      return false;
    }
    return true;
  }

  /// <summary>
  /// Attempts to switch operational state. Requires the structure complete, the
  /// converter constructed, and mechanical power to rotate. Returns a result the
  /// block can surface to the player.
  /// </summary>
  public bool TrySetState(
    IPlayer byPlayer,
    ConverterOpState newState,
    out string error
  ) {
    if (!CanOperate(out error))
      return false;

    if (OpState == newState)
      return true;

    OpState = newState;
    if (Api.Side == EnumAppSide.Server) {
      // Heavy door-style clunk as the vessel lever is set to fill / pour / hold,
      // layered over the grind of the heavy vessel rotating on its trunnions.
      ExSounds.Play(Api, Pos, ExSounds.CokeOvenDoorOpen, 0.9f);
      ExSounds.Play(Api, Pos.AddCopy(0, 0, 2), ExSounds.MetalGrinding, 0.7f);
      SyncConverter();
      MarkDirty(true);
    } else {
      ApplyControlPose();
    }
    return true;
  }

  #endregion

  #region Converter spawning

  /// <summary>
  /// Spawns the converter block in the correct cell/orientation, consuming the
  /// gears and rods from the player's hotbar. Returns false (with reason) if the
  /// converter already exists, the cell is blocked, or materials are missing.
  /// </summary>
  public bool TrySpawnConverter(IPlayer byPlayer, out string error) {
    error = "";
    if (IsConverterPresent()) {
      error = Lang.Get("smex:bessemer-err-converter-present");
      return false;
    }

    if (GetConverterBlock() is not Block converter)
      return false;

    BlockPos pos = PeripheralPos(ConverterLocal);
    Block existing = Api.World.BlockAccessor.GetBlock(pos);
    if (existing.Id != 0 && !existing.IsReplacableBy(converter)) {
      error = Lang.Get("smex:bessemer-err-converter-blocked");
      return false;
    }

    // The converter renders across a 3x3x3 volume reserved with invisible fillers; check every
    // cell is free first.
    int fillerAngle = ExOrientation.AngleFromSide(converter.Variant["side"]);
    var fillerCells = StructureFillers.FootprintCells(
      (IFillerHost)converter,
      pos,
      fillerAngle
    );
    if (!StructureFillers.CanPlace(Api.World, fillerCells)) {
      error = Lang.Get("smex:bessemer-err-converter-blocked");
      return false;
    }

    // Creative builders get the vessel for free - no gears/rods needed.
    bool isCreative =
      byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    if (!isCreative && !HasSpawnMaterials(byPlayer)) {
      error = Lang.Get(
        "smex:bessemer-err-materials",
        SmexValues.BessemerRequiredGears,
        SmexValues.BessemerRequiredRods
      );
      return false;
    }

    if (Api.Side != EnumAppSide.Server)
      return true;

    Api.World.BlockAccessor.SetBlock(converter.BlockId, pos);
    if (
      Api.World.BlockAccessor.GetBlockEntity(pos)
      is BlockEntityConverterBessemer be
    )
      be.LinkControl(Pos);

    // Reserve the volume with fillers pointing back at the converter; breaking any breaks it.
    StructureFillers.PlaceFillers(Api.World, pos, fillerCells);

    if (!isCreative)
      ConsumeSpawnMaterials(byPlayer);
    return true;
  }

  private Block? GetConverterBlock() {
    string side = Block.Variant["side"];
    return Api.World.GetBlock(
      new AssetLocation("smex:converterbessemer-" + side)
    );
  }

  // Spawn materials are drawn from the hotbar only (the player presents them), unlike engine repairs.
  // The converter vessel is gated on a smithable iron/steel large gear so it stays
  // buildable in worlds where looted rusty gears can't be obtained.
  private static bool IsSpawnGear(ItemStack stack) =>
    stack.Collectible?.Code?.ToString()
      is "ppex:largegear-iron"
        or "ppex:largegear-steel";

  private static bool IsSpawnRod(ItemStack stack) =>
    stack.Collectible?.Code?.ToString() is "game:rod-iron" or "game:rod-steel";

  private bool HasSpawnMaterials(IPlayer byPlayer) =>
    ExInventory.CountHotbar(byPlayer, IsSpawnGear)
      >= SmexValues.BessemerRequiredGears
    && ExInventory.CountHotbar(byPlayer, IsSpawnRod)
      >= SmexValues.BessemerRequiredRods;

  private void ConsumeSpawnMaterials(IPlayer byPlayer) {
    ExInventory.TakeHotbar(
      byPlayer,
      IsSpawnGear,
      SmexValues.BessemerRequiredGears
    );
    ExInventory.TakeHotbar(
      byPlayer,
      IsSpawnRod,
      SmexValues.BessemerRequiredRods
    );
  }

  #endregion

  #region Converter break handoff

  /// <summary>
  /// Called by the converter block when it is broken. Returns the solidified
  /// drops (bits/slag) to scatter, and clears the charge regardless.
  /// </summary>
  public ItemStack? OnConverterBroken() {
    ItemStack? drops = null;
    if (_solidified && _content != null && _contentUnits > 0)
      drops = BuildSolidifiedDrops();
    else if (_scrapUnits > 0 && _content != null) {
      // Scrap that never melted comes back whole - it was already solid.
      drops = BuildRecoveryDrops(_scrapUnits);
    }

    _content = null;
    _contentUnits = 0;
    _scrapUnits = 0;
    _processSeconds = 0f;
    _processTemp = 0f;
    _convSpeed = 0f;
    _solidified = false;
    OpState = ConverterOpState.Normal;
    MarkDirty(true);
    return drops;
  }

  private ItemStack? BuildSolidifiedDrops() {
    // Breaking the vessel mangles part of the charge: a few units less than chiselling recovers.
    // Unmelted scrap is not mangled.
    int randLoss = Random.Shared.Next(3) * SmexValues.MoltenUnitsPerBit;
    int remaining = _contentUnits - randLoss;
    if (remaining <= 0)
      remaining = _contentUnits;
    return BuildRecoveryDrops(remaining + _scrapUnits);
  }

  /// <summary>
  /// Builds the metal-bit recovery stack for <paramref name="units"/> of the current charge (5 units
  /// per bit), carrying the charge temperature; falls back to slag for a non-metal charge. Shared with
  /// the canal/barrel chisel drops via <see cref="MoltenChisel.BuildRecovery"/>.
  /// </summary>
  private ItemStack? BuildRecoveryDrops(int units) =>
    MoltenChisel.BuildRecovery(
      Api.World,
      _content!.Collectible.Code,
      MoltenMetal.GetTemperature(Api.World, _content),
      units,
      slagFallback: true
    );

  #endregion

  #region Chisel-out (small hardened residue)

  /// <summary>True when a solidified charge is present (latched below the melting point).</summary>
  public bool HasSolidifiedCharge =>
    _solidified && _content != null && _contentUnits > 0;

  /// <summary>True when the charge has cooled below the hardened (chisellable) threshold.</summary>
  public bool ChargeIsHardened =>
    _content != null
    && _contentUnits > 0
    && MoltenMetal.IsHardened(Api.World, _content);

  /// <summary>
  /// True when a small, fully-hardened residue can be chiselled out of the vessel - rather than
  /// having to break the whole converter to recover a charge that solidified mid-pour. Requires the
  /// charge solidified, cooled to hardened, and below <see cref="ChiselMaxFraction"/> of capacity.
  /// </summary>
  public bool CanChiselOut() =>
    HasSolidifiedCharge
    && ChargeIsHardened
    && _contentUnits < ChiselMaxFraction * CapacityUnits;

  /// <summary>
  /// Server-side: chips the hardened residue out of the vessel, returns the recovered metal-bit drop
  /// (full amount, no break loss), and clears the charge. Returns <c>null</c> off-server or when the
  /// charge is not chiselable.
  /// </summary>
  public ItemStack? ChiselOutContent() {
    if (Api?.Side != EnumAppSide.Server || !CanChiselOut())
      return null;

    ItemStack? recovered = BuildRecoveryDrops(_contentUnits + _scrapUnits);
    _content = null;
    _contentUnits = 0;
    _scrapUnits = 0;
    _processSeconds = 0f;
    _processTemp = 0f;
    _convSpeed = 0f;
    _solidified = false;
    SyncConverter();
    MarkDirty(true);
    return recovered;
  }

  // Guides the player through clearing a frozen charge, the same way a clogged canal cell does:
  //  - too large a residue to chisel  -> break the vessel to salvage it;
  //  - small, but still too hot        -> wait for it to harden, then chisel (it can't be chipped yet);
  //  - small and fully hardened        -> chisel it out from the upper hatch.
  private string SolidifiedStatus() {
    if (_contentUnits >= ChiselMaxFraction * CapacityUnits)
      return Lang.Get("smex:bessemer-status-solidified");

    return Lang.Get(
      ChargeIsHardened
        ? "smex:bessemer-status-chiselout"
        : "smex:bessemer-status-coolingtochisel"
    );
  }

  #endregion

  #region Animation

  private void ApplyControlPose() {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    _animatable.animUtil.StopAnimation("filling");
    _animatable.animUtil.StopAnimation("pouring");

    string? code = OpState switch {
      ConverterOpState.Filling => "filling",
      ConverterOpState.Pouring => "pouring",
      _ => null,
    };
    if (code != null)
      _animatable.animUtil.StartAnimation(
        new AnimationMetaData {
          Animation = code,
          Code = code,
          AnimationSpeed = 3.0f, // lever pull - quick
          EaseInSpeed = 8f,
          EaseOutSpeed = 8f,
        }.Init()
      );
  }

  private void SyncConverter() {
    GetConverter()?.UpdateMirror(_solidified, _contentUnits, OpState);
  }

  protected override void OnStructureCompleted() => SyncConverter();

  protected override void OnStructureLost() {
    if (OpState != ConverterOpState.Normal) {
      OpState = ConverterOpState.Normal;
      ApplyControlPose();
    }
  }

  #endregion

  #region Abstract impls

  protected override void UpdateStructureRotation() {
    if (Block == null)
      return;

    // The control's local frame faces opposite the stored angle: init at angle + 180, matched by
    // GetGlobalPos (the +180 convention).
    SetStructureAngle(
      ExOrientation.AngleFromSide(Block.Variant["side"]),
      initAngleOffset: 180
    );
  }

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("smex:bessemer-err-incomplete-count", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("smex:bessemer-complete");

  #endregion

  #region HUD

  /// <summary>
  /// Brief setup guidance on the control. The live operational readout (charge,
  /// process, power, status) is shown on the converter vessel itself - see
  /// <see cref="AppendStructureState"/>, which the converter forwards to.
  /// </summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    if (!StructureComplete) {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-incomplete"));
      return;
    }
    if (!IsConverterConstructed()) {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-notbuilt"));
      return;
    }
    // The vessel shows the full readout; the control just surfaces power state (wired to the
    // transmission directly under it).
    dsc.AppendLine(
      Lang.Get(
        "smex:bessemer-info-power",
        HasPower()
          ? Lang.Get("smex:bessemer-power-on")
          : Lang.Get("smex:bessemer-power-off")
      )
    );
  }

  /// <summary>
  /// Builds the full operational readout for the converter. Invoked by the
  /// converter vessel's <c>GetBlockInfo</c> so the player reads the state off the
  /// big block they are naturally looking at, rather than the small control.
  /// </summary>
  public void AppendStructureState(IPlayer forPlayer, StringBuilder dsc) {
    if (!StructureComplete) {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-incomplete"));
      return;
    }
    if (!IsConverterConstructed()) {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-notbuilt"));
      return;
    }
    if (!IsGasIntakeAligned()) {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-intake-misaligned"));
      return;
    }
    if (!IsTransmissionAligned()) {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-transmission-misaligned"));
      return;
    }

    // Only the charge (amount/metal/temp) is unique info; empty/solidified are in the status line.
    if (_content != null && _contentUnits > 0) {
      float temp = _content.Collectible.GetTemperature(Api.World, _content);
      string path = _content.Collectible.Code.Path;
      string metal = path.StartsWith("ingot-") ? path[6..] : path;
      dsc.AppendLine(
        Lang.Get(
          "smex:bessemer-info-charge",
          _contentUnits,
          CapacityUnits,
          metal,
          ExMeasure.Temperature(temp)
        )
      );
    }

    // Scrap is limited by the heat it costs, so both figures are needed to judge the next handful.
    if (_scrapUnits > 0)
      dsc.AppendLine(
        Lang.Get(
          "smex:bessemer-info-scrap",
          _scrapUnits,
          ExMeasure.Temperature(
            SmexValues.BessemerColdScrapLossCoefficient * _scrapUnits
          )
        )
      );

    if (_convSpeed > 0f)
      dsc.AppendLine(
        Lang.Get(
          "smex:bessemer-info-blowrate",
          _convSpeed.ToString("0.0#"),
          ExMeasure.Temperature(_processTemp)
        )
      );

    dsc.AppendLine(Lang.Get("smex:bessemer-info-status", _status));
  }

  private void SetStatus(string status) {
    if (_status == status)
      return;
    _status = status;
    if (Api?.Side == EnumAppSide.Server)
      MarkDirty();
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetInt("opState", (int)OpState);
    tree.SetItemstack("content", _content);
    tree.SetInt("contentUnits", _contentUnits);
    tree.SetInt("scrapUnits", _scrapUnits);
    tree.SetFloat("processSeconds", _processSeconds);
    tree.SetFloat("processTemp", _processTemp);
    tree.SetFloat("convSpeed", _convSpeed);
    tree.SetBool("solidified", _solidified);
    tree.SetString("status", _status);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    var prevState = OpState;
    OpState = (ConverterOpState)tree.GetInt("opState");
    _content = tree.GetItemstack("content");
    _content?.ResolveBlockOrItem(worldForResolving);
    _contentUnits = tree.GetInt("contentUnits");
    _scrapUnits = tree.GetInt("scrapUnits");
    _processSeconds = tree.GetFloat("processSeconds");
    _processTemp = tree.GetFloat("processTemp");
    _convSpeed = tree.GetFloat("convSpeed");
    _solidified = tree.GetBool("solidified");
    _status = tree.GetString("status", Lang.Get("smex:bessemer-status-idle"));

    if (Api?.Side == EnumAppSide.Client && prevState != OpState)
      ApplyControlPose();
  }

  #endregion
}

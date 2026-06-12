using System;
using System.Text;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
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
[EntityRegister]
public class BlockEntityConverterControl : BlockEntityMultiblockStructure
{
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
  private static float ProcessHoldTemp => SmexValues.BessemerProcessTemperature;
  private static float PowerSpeedThreshold =>
    SmexValues.BessemerPowerSpeedThreshold;

  private const string IronCode = "game:ingot-iron";
  private const string SteelCode = "game:ingot-steel";
  #endregion

  #region Operational + charge state
  /// <summary>Current player-selected operating mode of the converter.</summary>
  public ConverterOpState OpState { get; private set; } =
    ConverterOpState.Normal;

  private ItemStack? _content;
  private int _contentUnits;
  private float _processSeconds;
  private bool _solidified;
  private string _status = Lang.Get("smex:bessemer-status-idle");

  private BlockNetworkModSystem? _netSystem;
  private BEBehaviorAnimatable? _animatable;

  // Sound throttles (world-elapsed ms): the refining roar and the molten
  // filling/pouring hiss are looping ambience, gated so the per-second tick
  // doesn't spam audio. Filling and pouring are mutually exclusive, so they
  // share one throttle.
  private long _lastProcessSoundMs;
  private long _lastFireSoundMs;
  private long _lastMoltenSoundMs;
  #endregion

  #region Lifecycle

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _netSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    _animatable = GetBehavior<BEBehaviorAnimatable>();

    // Establish the structure angle up front so GetGlobalPos resolves
    // peripherals correctly on the very first production tick (which can fire
    // before the slower completion tick runs UpdateStructureRotation).
    UpdateStructureRotation();

    if (api is ICoreClientAPI capi && _animatable != null)
    {
      Shape? shape = capi
        .Assets.TryGet(
          Block
            .Shape.Base.Clone()
            .WithPathPrefixOnce("shapes/")
            .WithPathAppendixOnce(".json")
        )
        ?.ToObject<Shape>();
      if (shape != null)
        // Use the BlockEntityAnimationUtil overload (CreateMesh): rotation is
        // applied only by the renderer, not baked into the mesh. Initialize
        // ShapeAndAnimator does both and would rotate the control 180° off.
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

  protected override void OnProductionTick(float dt)
  {
    if (!StructureComplete || !IsConverterConstructed())
    {
      SetStatus(Lang.Get("smex:bessemer-status-notbuilt"));
      return;
    }

    if (!IsGasIntakeAligned())
    {
      SetStatus(Lang.Get("smex:bessemer-status-misaligned"));
      return;
    }

    if (!IsTransmissionAligned())
    {
      SetStatus(Lang.Get("smex:bessemer-status-transmission-misaligned"));
      return;
    }

    UpdateSolidified();

    switch (OpState)
    {
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

  private void TickNormal(float dt)
  {
    // Idle while holding the charge; run the bessemer refining process if the
    // charge is molten iron and the gas intake is fed blast.
    if (_content == null || _contentUnits <= 0)
    {
      SetStatus(Lang.Get("smex:bessemer-status-empty"));
      return;
    }

    if (_solidified)
    {
      SetStatus(Lang.Get("smex:bessemer-status-solidified"));
      return;
    }

    // Refining only applies to molten iron. Once it has become steel the charge
    // just waits to be poured; any other metal is foreign. In both cases we stop
    // here — no blast is drawn, the process clock stops, and (crucially) no
    // process particles are emitted once the conversion has finished.
    if (!IsMoltenIron())
    {
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

    // Refining: requires blast from the gas intake (consumes BessemerBlastPerSecond L/s).
    float consumed = TryConsumeBlast(BlastPerSecond * dt);
    if (consumed <= 0f)
    {
      SetStatus(
        Lang.Get("smex:bessemer-status-refining-paused", FormatProgress())
      );
      return;
    }

    // Blast halts cooling and keeps the bath at working temperature.
    HoldTemperature(dt);

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

    // Crackling fire layered over the blast while iron is being blown — the
    // burning carbon roaring off in the bessemer. Offset from the embers throttle
    // so the two loops overlap rather than firing in lockstep.
    ExSounds.PlayThrottled(
      Api,
      Pos.AddCopy(0, 0, 2),
      ExSounds.Fire,
      ref _lastFireSoundMs,
      3000,
      1.5f
    );

    _processSeconds += dt;
    if (_processSeconds >= ProcessDurationSec)
      CompleteRefining();
    else
      SetStatus(Lang.Get("smex:bessemer-status-refining", FormatProgress()));

    MarkDirty();
  }

  private void TickFilling(float dt)
  {
    if (_solidified)
    {
      SetStatus(Lang.Get("smex:bessemer-status-solidified"));
      return;
    }

    var inputCell = GetMoltenCell(InputTapLocal);

    // Respect the tap's open/closed state. A closed tap's cell keeps receiving
    // metal from the network (IsPouring only gates the tap's own draining), so we
    // must check it here or the vessel would fill straight through a shut tap.
    if (inputCell is BlockEntityMoltenCanalTap { IsPouring: false })
    {
      SetStatus(Lang.Get("smex:bessemer-status-filling-tapclosed"));
      return;
    }

    if (inputCell == null || !inputCell.HasMoltenMetal)
    {
      SetStatus(Lang.Get("smex:bessemer-status-filling-nometal"));
      return;
    }

    if (_contentUnits >= CapacityUnits)
    {
      SetStatus(Lang.Get("smex:bessemer-status-filling-full"));
      return;
    }

    // Only accept a single metal type at a time.
    if (
      _content != null
      && _content.Collectible.Code.ToString() != inputCell.CellMetalType
    )
    {
      SetStatus(Lang.Get("smex:bessemer-status-filling-mismatch"));
      return;
    }

    int space = CapacityUnits - _contentUnits;
    int toDrain = Math.Min(inputCell.CellAmount, space);
    if (toDrain <= 0)
      return;

    // Capture metal identity/temperature before draining empties the cell.
    string type = inputCell.CellMetalType;
    float temp = inputCell.CellTemperature;

    float drained = inputCell.DrainMetal(toDrain);
    if (drained <= 0f)
      return;

    _content ??= MoltenMetal.CreateStack(Api.World, type, temp);
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

  private void TickPouring(float dt)
  {
    if (_content == null || _contentUnits <= 0)
    {
      SetStatus(Lang.Get("smex:bessemer-status-pouring-empty"));
      return;
    }

    if (_solidified)
    {
      SetStatus(Lang.Get("smex:bessemer-status-solidified"));
      return;
    }

    var outputCell = GetMoltenCell(OutputStartLocal);
    if (outputCell == null)
    {
      SetStatus(Lang.Get("smex:bessemer-status-pouring-nocanal"));
      return;
    }

    int amount = Math.Min(
      _contentUnits,
      Math.Max(1, (int)(CapacityUnits * dt))
    );
    float accepted = outputCell.PushMetal(amount, _content, Api.World);
    if (accepted <= 0f)
    {
      // Output canal is brim-full: keep bathing it in our hot content so it stays
      // molten and keeps feeding downstream, instead of cooling to a plug while we
      // wait for it to drain. Mirrors the furnace tap's full-cell heat soak.
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
    if (_contentUnits <= 0)
    {
      _contentUnits = 0;
      _content = null;
      _processSeconds = 0f;
      SetStatus(Lang.Get("smex:bessemer-status-emptied"));
    }
    else
    {
      SetStatus(
        Lang.Get("smex:bessemer-status-pouring", _contentUnits, CapacityUnits)
      );
    }
    MarkDirty();
  }

  private void CompleteRefining()
  {
    float temp = MoltenMetal.GetTemperature(Api.World, _content!);
    ItemStack? steelStack = MoltenMetal.CreateStack(Api.World, SteelCode, temp);
    if (steelStack == null)
      return;
    _content = steelStack;
    _processSeconds = ProcessDurationSec;
    SetStatus(Lang.Get("smex:bessemer-status-steelready"));
    MarkDirty();
  }

  #endregion

  #region Temperature handling

  private void HoldTemperature(float dt)
  {
    if (_content == null)
      return;
    float temp = MoltenMetal.GetTemperature(Api.World, _content);
    float target = Math.Max(temp, ProcessHoldTemp);
    MoltenMetal.SetTemperature(Api.World, _content, target);
  }

  private void UpdateSolidified()
  {
    if (_content == null || _contentUnits <= 0)
    {
      if (_solidified)
      {
        _solidified = false;
        SyncConverter();
      }
      return;
    }

    bool nowSolid =
      MoltenMetal.GetTemperature(Api.World, _content)
      < MoltenMetal.MeltingPointOf(Api.World, _content);
    if (nowSolid != _solidified)
    {
      _solidified = nowSolid;
      if (nowSolid)
        ExSounds.Play(Api, Pos, ExSounds.Extinguish, 0.7f);
      SyncConverter();
      MarkDirty();
    }
  }

  #endregion

  #region Peripheral access
  // The converter's local frame faces opposite the structure angle, so its global
  // mapping is the shared rotation taken at _currentAngle + 180 (see the cowper/
  // converter +180 convention).
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

  private float TryConsumeBlast(float amount)
  {
    // The intake is a fixed connector, not a network node — the blast network
    // lives in the cell on the other side of its connector face, not in the
    // intake cell itself.
    BlockPos intakePos = PeripheralPos(GasIntakeLocal);
    if (
      Api.World.BlockAccessor.GetBlock(intakePos)
      is not Blocks.BlockConverterIntake intake
    )
      return 0f;
    // Only draw blast from a network whose pipe actually presents a connector back at the
    // intake's connector face — air merely sitting in a pipe routed past the intake without
    // a connector facing it is not plumbed into the converter.
    if (
      _netSystem?.GetConnectedNetworkAcross(
        Api.World.BlockAccessor,
        intakePos,
        intake.ConnectorFace
      )
      is not PipeNetwork pipeNet
    )
      return 0f;
    // "Blast" is now air at or above the blast threshold pressure (≥ 3 atm).
    if (
      pipeNet.State?.MediumType != "Air"
      || pipeNet.State.Pressure < SmexValues.BlastPressureThreshold
    )
      return 0f;
    return pipeNet.TryConsumeGas(amount, Api.World.BlockAccessor);
  }

  /// <summary>True if the transmission's mechanical network is turning.</summary>
  public bool HasPower()
  {
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
  /// The gas intake must face the same way as the control (its <c>side</c> variant
  /// matches), otherwise its blast connector won't line up with the vessel. The
  /// multiblock check accepts any orientation, so we validate here.
  /// </summary>
  public bool IsGasIntakeAligned()
  {
    Block intake = Api.World.BlockAccessor.GetBlock(
      PeripheralPos(GasIntakeLocal)
    );
    if (intake is not Blocks.BlockConverterIntake)
      return false;

    return intake.Variant["side"] == (Block.Variant["side"] ?? "north");
  }

  /// <summary>
  /// The transmission must face the same way as the control (its "side" variant
  /// matches), otherwise the axle connector won't line up under the vessel. The
  /// multiblock check accepts any orientation, so we validate here — exactly like
  /// the gas intake.
  /// </summary>
  public bool IsTransmissionAligned()
  {
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

  private string FormatProgress()
  {
    int pct = (int)(100f * _processSeconds / Math.Max(1f, ProcessDurationSec));
    return $"{GameMath.Clamp(pct, 0, 100)}%";
  }

  #endregion

  #region Player-driven state transitions

  /// <summary>
  /// Validates that the converter is in a state where the player can change its
  /// operating mode: structure complete, vessel constructed, peripherals aligned,
  /// and mechanical power present. Returns false with a player-facing reason.
  /// </summary>
  public bool CanOperate(out string error)
  {
    error = "";
    if (!StructureComplete)
    {
      error = Lang.Get("smex:bessemer-err-incomplete");
      return false;
    }
    if (!IsConverterConstructed())
    {
      error = Lang.Get("smex:bessemer-err-notbuilt");
      return false;
    }
    if (!IsGasIntakeAligned())
    {
      error = Lang.Get("smex:bessemer-err-intake-misaligned");
      return false;
    }
    if (!IsTransmissionAligned())
    {
      error = Lang.Get("smex:bessemer-err-transmission-misaligned");
      return false;
    }
    if (!HasPower())
    {
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
  )
  {
    if (!CanOperate(out error))
      return false;

    if (OpState == newState)
      return true;

    OpState = newState;
    if (Api.Side == EnumAppSide.Server)
    {
      // Heavy door-style clunk as the vessel lever is set to fill / pour / hold,
      // layered over the grind of the heavy vessel rotating on its trunnions.
      ExSounds.Play(Api, Pos, ExSounds.CokeOvenDoorOpen, 0.9f);
      ExSounds.Play(Api, Pos.AddCopy(0, 0, 2), ExSounds.MetalGrinding, 0.7f);
      SyncConverter();
      MarkDirty(true);
    }
    else
    {
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
  public bool TrySpawnConverter(IPlayer byPlayer, out string error)
  {
    error = "";
    if (IsConverterPresent())
    {
      error = Lang.Get("smex:bessemer-err-converter-present");
      return false;
    }

    if (GetConverterBlock() is not Block converter)
      return false;

    BlockPos pos = PeripheralPos(ConverterLocal);
    Block existing = Api.World.BlockAccessor.GetBlock(pos);
    if (existing.Id != 0 && !existing.IsReplacableBy(converter))
    {
      error = Lang.Get("smex:bessemer-err-converter-blocked");
      return false;
    }

    // The converter is a single block that renders across a 3x3x3 volume; that
    // volume is reserved with invisible filler blocks so the player can't walk
    // through it or build into it. Make sure every cell is free first.
    int fillerAngle = ExOrientation.AngleFromSide(converter.Variant["side"]);
    var fillerCells = StructureFillers.FootprintCells(
      converter,
      pos,
      fillerAngle
    );
    if (!StructureFillers.CanPlace(Api.World, fillerCells))
    {
      error = Lang.Get("smex:bessemer-err-converter-blocked");
      return false;
    }

    // Creative builders get the vessel for free — no gears/rods needed.
    bool isCreative =
      byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;

    if (!isCreative && !HasSpawnMaterials(byPlayer))
    {
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

    // Reserve the rest of the 3x3x3 volume with filler blocks pointing back at
    // the converter; breaking any of them breaks the converter.
    StructureFillers.PlaceFillers(Api.World, pos, fillerCells);

    if (!isCreative)
      ConsumeSpawnMaterials(byPlayer);
    return true;
  }

  private Block? GetConverterBlock()
  {
    string side = Block.Variant["side"];
    return Api.World.GetBlock(
      new AssetLocation("smex:converterbessemer-" + side)
    );
  }

  // Spawn materials are deliberately drawn from the hotbar only (the player presents
  // them), unlike engine repairs which search the whole inventory.
  private static bool IsSpawnGear(ItemStack stack) =>
    stack.Collectible?.Code?.ToString() == "game:gear-rusty";

  private static bool IsSpawnRod(ItemStack stack) =>
    stack.Collectible?.Code?.ToString() is "game:rod-iron" or "game:rod-steel";

  private bool HasSpawnMaterials(IPlayer byPlayer) =>
    ExInventory.CountHotbar(byPlayer, IsSpawnGear)
      >= SmexValues.BessemerRequiredGears
    && ExInventory.CountHotbar(byPlayer, IsSpawnRod)
      >= SmexValues.BessemerRequiredRods;

  private void ConsumeSpawnMaterials(IPlayer byPlayer)
  {
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
  public ItemStack? OnConverterBroken()
  {
    ItemStack? drops = null;
    if (_solidified && _content != null && _contentUnits > 0)
      drops = BuildSolidifiedDrops();

    _content = null;
    _contentUnits = 0;
    _processSeconds = 0f;
    _solidified = false;
    OpState = ConverterOpState.Normal;
    MarkDirty(true);
    return drops;
  }

  private ItemStack? BuildSolidifiedDrops()
  {
    // Recover ~part of the charge as metal bits, or slag for non-metal charges.
    int randLoss = Random.Shared.Next(3) * 5;
    int remaining = _contentUnits - randLoss;
    if (remaining <= 0)
      remaining = _contentUnits;

    int count = Math.Max(1, remaining / 5);
    var loc = MoltenNetwork.SolidDropLocation(_content!.Collectible.Code);
    Item? item = Api.World.GetItem(loc);
    if (item == null)
    {
      Item? slag = Api.World.GetItem(new AssetLocation("smex:slag"));
      return slag != null ? new ItemStack(slag, count) : null;
    }
    return new ItemStack(item, count);
  }

  #endregion

  #region Animation

  private void ApplyControlPose()
  {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    _animatable.animUtil.StopAnimation("filling");
    _animatable.animUtil.StopAnimation("pouring");

    string? code = OpState switch
    {
      ConverterOpState.Filling => "filling",
      ConverterOpState.Pouring => "pouring",
      _ => null,
    };
    if (code != null)
      _animatable.animUtil.StartAnimation(
        new AnimationMetaData
        {
          Animation = code,
          Code = code,
          AnimationSpeed = 3.0f, // lever pull — quick
          EaseInSpeed = 8f,
          EaseOutSpeed = 8f,
        }.Init()
      );
  }

  private void SyncConverter()
  {
    GetConverter()?.UpdateMirror(_solidified, _contentUnits, OpState);
  }

  protected override void OnStructureCompleted() => SyncConverter();

  protected override void OnStructureLost()
  {
    if (OpState != ConverterOpState.Normal)
    {
      OpState = ConverterOpState.Normal;
      ApplyControlPose();
    }
  }

  #endregion

  #region Abstract impls

  protected override void UpdateStructureRotation()
  {
    if (Block == null)
      return;

    // The control's local frame faces opposite the stored angle: the structure is
    // initialised at angle + 180 while peripherals compensate the same way in
    // GetGlobalPos (see the +180 convention note there).
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
  /// process, power, status) is shown on the converter vessel itself — see
  /// <see cref="AppendStructureState"/>, which the converter forwards to.
  /// </summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    if (!StructureComplete)
    {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-incomplete"));
      return;
    }
    if (!IsConverterConstructed())
    {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-notbuilt"));
      return;
    }
    // Converter is in place — its full live readout is displayed on the vessel.
    // The control just surfaces the mechanical-power state here, since power is
    // wired to the transmission directly under this block.
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
  public void AppendStructureState(IPlayer forPlayer, StringBuilder dsc)
  {
    if (!StructureComplete)
    {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-incomplete"));
      return;
    }
    if (!IsConverterConstructed())
    {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-notbuilt"));
      return;
    }
    if (!IsGasIntakeAligned())
    {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-intake-misaligned"));
      return;
    }
    if (!IsTransmissionAligned())
    {
      dsc.AppendLine(Lang.Get("smex:bessemer-info-transmission-misaligned"));
      return;
    }

    // Only the actual charge (amount/metal/temp) is unique info; the empty and
    // solidified states are already conveyed by the status line below, so don't
    // repeat them here.
    if (_content != null && _contentUnits > 0)
    {
      float temp = _content.Collectible.GetTemperature(Api.World, _content);
      string path = _content.Collectible.Code.Path;
      string metal = path.StartsWith("ingot-") ? path[6..] : path;
      dsc.AppendLine(
        Lang.Get(
          "smex:bessemer-info-charge",
          _contentUnits,
          CapacityUnits,
          metal,
          temp
        )
      );
    }

    dsc.AppendLine(Lang.Get("smex:bessemer-info-status", _status));
  }

  private void SetStatus(string status)
  {
    if (_status == status)
      return;
    _status = status;
    if (Api?.Side == EnumAppSide.Server)
      MarkDirty();
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetInt("opState", (int)OpState);
    tree.SetItemstack("content", _content);
    tree.SetInt("contentUnits", _contentUnits);
    tree.SetFloat("processSeconds", _processSeconds);
    tree.SetBool("solidified", _solidified);
    tree.SetString("status", _status);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    var prevState = OpState;
    OpState = (ConverterOpState)tree.GetInt("opState");
    _content = tree.GetItemstack("content");
    _content?.ResolveBlockOrItem(worldForResolving);
    _contentUnits = tree.GetInt("contentUnits");
    _processSeconds = tree.GetFloat("processSeconds");
    _solidified = tree.GetBool("solidified");
    _status = tree.GetString("status", Lang.Get("smex:bessemer-status-idle"));

    if (Api?.Side == EnumAppSide.Client && prevState != OpState)
      ApplyControlPose();
  }

  #endregion
}

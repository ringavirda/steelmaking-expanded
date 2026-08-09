using System;
using System.Text;
using ExpandedLib.Blocks.Construction;
using ExpandedLib.Blocks.Machines;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.MpPump.Blocks;
using PipesAndPowerExpanded.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.MpPump.BlockEntities;

/// <summary>
/// The mechanical (walking-beam) fluid pump. Driven from the mechanical power network instead of
/// from steam, so it can fill a boiler whose fire is out - the device that lets a water-powered
/// shop raise its first boiler without a bootstrap. Like every pump here it is a transfer device,
/// not a source: the <see cref="BlockEntityFluidIntake"/> on the source line is the generator, and
/// each tick the standing source water is moved out first and the intake refills it afterwards.
/// </summary>
[BlockEntityRegister]
public class BlockEntityMpFluidPump : BlockEntity {
  /// <summary>Server-side work interval; matches the pipe network's own per-second tick.</summary>
  private const int WorkIntervalMs = 1000;

  private long _serverTickId;
  private long _clientTickId;

  /// <summary>Axle speed sampled on the last work tick; synced so the client can animate.</summary>
  private float _lastSpeed;

  /// <summary>True while the pump has an active intake on its source line and is moving water.</summary>
  private bool _drawingWater;

  private BEBehaviorAnimatable? _animatable;
  private ExRightClickConstructable? _rcc;
  private bool _animatorReady;
  private bool _animRunning;
  private ILoadedSound? _waterSound;

  private BlockMpFluidPump? PumpBlock => Block as BlockMpFluidPump;

  /// <summary>True once every construction stage is built; an unfinished frame does no work.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Fraction of the rated rate the beam is currently moving.</summary>
  public float OutputFraction => SpeedFraction(_lastSpeed);

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _rcc = GetBehavior<ExRightClickConstructable>();

    if (api.Side == EnumAppSide.Server) {
      _serverTickId = RegisterGameTickListener(OnServerTick, WorkIntervalMs);
    } else {
      _animatable = GetBehavior<BEBehaviorAnimatable>();
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;
      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      _animRunning = OutputFraction > 0f;
      ApplyAnim(_animRunning);
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
    }
  }

  #region Server work

  private void OnServerTick(float dt) {
    if (!IsConstructed)
      return;

    float speed = DriveSpeed();
    if (speed != _lastSpeed) {
      _lastSpeed = speed;
      MarkDirty();
    }
    DoWork(speed, dt);
  }

  /// <summary>
  /// Moves <paramref name="dt"/> seconds of water from the source line to the delivery line at
  /// axle speed <paramref name="speed"/>, and returns the litres that actually landed. The
  /// standing source water is transferred out first and the intake refills it, so the source pipe
  /// still reads as a water line at broadcast time. Public so the balance can be driven without a
  /// mechanical network.
  /// </summary>
  public float DoWork(float speed, float dt) {
    float fraction = SpeedFraction(speed);
    if (fraction <= 0f || dt <= 0f) {
      SetDrawing(false);
      return 0f;
    }

    var ba = Api.World.BlockAccessor;
    PipeNetwork? sourceNet = SourceNetwork();
    PipeNetwork? deliveryNet = DeliveryNetwork();

    // The intake is the generator; with none on the source line the beam works and moves nothing.
    BlockEntityFluidIntake? intake = FindIntake(sourceNet);
    SetDrawing(intake != null);
    if (intake == null)
      return 0f;

    float amount = PpexValues.MpPumpWaterPerSecond * fraction * dt;
    float move = Math.Min(amount, OutputFreeCapacity(deliveryNet));
    float drawn = sourceNet?.TryConsumeLiquid(move, ba) ?? 0f;
    if (drawn > 0f)
      // The beam lifts the same column however fast it runs, so the head is speed-independent.
      deliveryNet?.TryProduceLiquid(
        drawn,
        20f,
        PpexValues.MpPumpDeliveryPressure,
        ba
      );

    intake.ProduceWater(amount, 20f, ba);
    return drawn;
  }

  /// <summary>The source (intake line) network, drawn from under the far, low filler cell.</summary>
  private PipeNetwork? SourceNetwork() =>
    PumpBlock is { } block
      ? NetworkAcross(block.SourceWorldPos(Pos), BlockMpFluidPump.SourceFace)
      : null;

  /// <summary>The delivery network, across the output face from the far, high filler cell.</summary>
  private PipeNetwork? DeliveryNetwork() =>
    PumpBlock is { } block
      ? NetworkAcross(block.OutletWorldPos(Pos), block.OutputFace)
      : null;

  /// <summary>
  /// The pipe network in the cell across <paramref name="face"/> from <paramref name="portCell"/>,
  /// or null unless a pipe there presents a connector facing back. The port cells are fillers, so
  /// this walks out from them rather than from the pump's own position.
  /// </summary>
  private PipeNetwork? NetworkAcross(BlockPos portCell, BlockFacing face) {
    BlockPos pipePos = portCell.AddCopy(face);
    return
      Api.World.BlockAccessor.GetBlock(pipePos) is BlockNetworkNode pipe
      && pipe.HasConnectorAt(face.Opposite)
      ? this.NetworkAt<PipeNetwork>(pipePos)
      : null;
  }

  /// <summary>The driving axle's speed, or 0 when no axle is coupled.</summary>
  private float DriveSpeed() {
    var drive = GetBehavior<BEBehaviorMpPumpDrive>();
    return drive is { IsTurning: true } ? drive.DriveSpeed : 0f;
  }

  /// <summary>
  /// Fraction of the rated rate the pump moves at <paramref name="speed"/>: 0 at or below
  /// <see cref="PpexValues.MpPumpMinSpeed"/>, 1 at or above
  /// <see cref="PpexValues.MpPumpMaxSpeed"/>, linear between.
  /// </summary>
  public static float SpeedFraction(float speed) {
    float min = PpexValues.MpPumpMinSpeed;
    float max = PpexValues.MpPumpMaxSpeed;
    if (speed <= min)
      return 0f;
    if (max <= min)
      return 1f;
    return GameMath.Clamp((speed - min) / (max - min), 0f, 1f);
  }

  /// <summary>The first fluid intake on <paramref name="net"/> that can currently draw water, or <c>null</c>.</summary>
  private BlockEntityFluidIntake? FindIntake(PipeNetwork? net) {
    if (net == null)
      return null;
    var ba = Api.World.BlockAccessor;
    foreach (var p in net.Nodes) {
      if (
        ba.GetBlockEntity(p) is BlockEntityFluidIntake intake
        && intake.CanIntake
      )
        return intake;
    }
    return null;
  }

  /// <summary>Litres of water the delivery network can still accept.</summary>
  private static float OutputFreeCapacity(PipeNetwork? net) =>
    net == null
      ? 0f
      : net.Nodes.Count * PpexValues.LitresPerPipe - (net.State?.Volume ?? 0f);

  /// <summary>Updates the synced water-drawing flag, syncing to clients only on change.</summary>
  private void SetDrawing(bool drawing) {
    if (drawing == _drawingWater)
      return;
    _drawingWater = drawing;
    MarkDirty();
  }

  #endregion

  #region Client animation + sound

  private void OnClientTick(float dt) {
    bool running = OutputFraction > 0f;
    if (running != _animRunning) {
      _animRunning = running;
      ApplyAnim(running);
    }
    UpdateSounds();
  }

  private void OnConstructShapeChanged(CompositeShape cs) {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyAnim(_animRunning);
  }

  /// <summary>
  /// (Re)builds the animator to render exactly the construction stages built so far. A fresh shape
  /// is loaded each call - reusing one re-maps UVs into atlas space and stretches the textures.
  /// Leaves <see cref="_animatorReady"/> false if the shape fails to resolve, so a pose is never
  /// queued against a null animator.
  /// </summary>
  private void RebuildAnimator(string[]? selectiveElements) {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    MeshData meshData = _animatable.animUtil.CreateMesh(
      Block.Code.Path,
      null,
      out Shape resolvedShape,
      null,
      new TesselationMetaData { SelectiveElements = selectiveElements }
    );
    _animatable.animUtil.InitializeAnimator(
      Block.Code.Path,
      meshData,
      resolvedShape,
      new Vec3f(0, Block.Shape.rotateY, 0)
    );
    _animatorReady = _animatable.animUtil.animator != null;
  }

  /// <summary>
  /// Holds one animation at a time - <c>cycle</c> while the beam works, <c>idle</c> otherwise.
  /// Keeping one active stops the animator mesh vanishing.
  /// </summary>
  private void ApplyAnim(bool running) {
    if (_animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    util.StopAnimation(running ? "idle" : "cycle");
    util.StartAnimation(
      new AnimationMetaData {
        Animation = running ? "cycle" : "idle",
        Code = running ? "cycle" : "idle",
        AnimationSpeed = 1f,
        EaseInSpeed = 1f,
        EaseOutSpeed = 5f,
      }.Init()
    );
  }

  /// <summary>Runs the watering loop only while the pump is actually moving water.</summary>
  private void UpdateSounds() {
    if (Api is not ICoreClientAPI)
      return;

    if (_drawingWater) {
      _waterSound ??= ExSounds.CreateLoop(
        Api,
        Pos,
        ExSounds.Watering,
        volume: 0.6f,
        range: 16f
      );
      if (_waterSound?.IsPlaying == false)
        _waterSound.Start();
    } else if (_waterSound?.IsPlaying == true)
      _waterSound.Stop();
  }

  private void DisposeSounds() {
    _waterSound?.Stop();
    _waterSound?.Dispose();
    _waterSound = null;
  }

  #endregion

  #region Persistence + lifecycle

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetFloat("pumpSpeed", _lastSpeed);
    tree.SetBool("drawingWater", _drawingWater);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _lastSpeed = tree.GetFloat("pumpSpeed");
    _drawingWater = tree.GetBool("drawingWater");
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed)
      return;

    float fraction = OutputFraction;
    if (fraction <= 0f) {
      dsc.AppendLine(Lang.Get("ppex:mpfluidpump-info-idle"));
      return;
    }

    dsc.AppendLine(
      Lang.Get(
        "ppex:mpfluidpump-info-running",
        ExMeasure.FlowRate(PpexValues.MpPumpWaterPerSecond * fraction),
        (int)(fraction * 100f)
      )
    );
    if (!_drawingWater)
      dsc.AppendLine(Lang.Get("ppex:mpfluidpump-info-nointake"));
  }

  public override void OnBlockRemoved() {
    UnregisterTicks();
    DisposeSounds();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    UnregisterTicks();
    DisposeSounds();
    base.OnBlockUnloaded();
  }

  private void UnregisterTicks() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    if (_serverTickId != 0) {
      UnregisterGameTickListener(_serverTickId);
      _serverTickId = 0;
    }
    if (_clientTickId != 0) {
      UnregisterGameTickListener(_clientTickId);
      _clientTickId = 0;
    }
  }

  #endregion
}

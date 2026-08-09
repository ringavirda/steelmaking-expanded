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
public class BlockEntityMpFluidPump : BlockEntity, IRenderer {
  /// <summary>
  /// Renders nothing. The pump registers itself only to get a per-render-frame callback, which is
  /// the one place the cycle can be pinned to the axle: the 250 ms client tick is four updates a
  /// second and shows as stepping. Runs before the opaque pass so the frame is already in step when
  /// the mesh is drawn.
  /// </summary>
  public double RenderOrder => 0.0;

  public int RenderRange => 64;

  public void OnRenderFrame(float dt, EnumRenderStage stage) =>
    LockCycleToAxle();

  public void Dispose() { }

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
  public float OutputPerSecond => OutputAt(_lastSpeed);

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
      _animRunning = OutputPerSecond > 0f;
      ApplyAnim(_animRunning);
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
      (api as ICoreClientAPI)?.Event.RegisterRenderer(
        this,
        EnumRenderStage.Before,
        "ppex-mppump-cycle"
      );
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
    float output = OutputAt(speed);
    if (output <= 0f || dt <= 0f) {
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

    float amount = output * dt;
    // A working beam with no delivery line still lifts water - it just throws it out of the bare
    // outlet. Showing that is the only way a player can tell a driven pump from a stalled one when
    // the delivery main is missing or has come off.
    if (deliveryNet == null)
      SpillFromOpenOutlet();

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
  /// Water (L/s) the pump moves at axle <paramref name="speed"/> - straight proportion, because the
  /// beam drives a fixed displacement per turn of the shaft. No threshold and no ceiling: a shaft
  /// barely turning moves a trickle, which is what a beam pump does. What limits the pump is the
  /// head it lifts, through <see cref="BEBehaviorMpPumpDrive.PumpResistance"/>, not a band drawn on
  /// its speed.
  /// </summary>
  public static float OutputAt(float speed) =>
    PpexValues.MpPumpLitresPerSpeed * GameMath.Max(0f, speed);

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
  /// <summary>
  /// Throws the lifted water out of the bare delivery port, with the splash to match - the same
  /// spill the engine makes when its condensate has nowhere to drain. Server-side: particles
  /// replicate to clients, and this runs on the work tick.
  /// </summary>
  private void SpillFromOpenOutlet() {
    if (Api is not { Side: EnumAppSide.Server } || PumpBlock is not { } block)
      return;

    ExParticles.WaterJet(
      Api.World,
      block.OutletWorldPos(Pos),
      block.OutputFace
    );
    ExSounds.SplashSound(Api.World, Pos);
  }

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
    bool running = OutputPerSecond > 0f;
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
  /// Pins the running <c>cycle</c> clip to the drive shaft's angle, so the gear, the connecting rod,
  /// the piston and the two valve flaps all move in step with the axle the player can see, at any
  /// speed, instead of free-running at a fixed rate. A no-op while the pump is idle - <c>idle</c> is
  /// the clip that is running then, and it has no cycle to lock.
  /// </summary>
  private void LockCycleToAxle() {
    if (!_animRunning || _animatable == null || !_animatorReady)
      return;
    if (GetBehavior<BEBehaviorMpPumpDrive>() is not { } drive)
      return;
    // Reversed: the clip turns its Axle element through +360 over the cycle, which runs against the
    // vanilla axle for a rising angle - the pump's shaft span the wrong way beside the axle driving
    // it.
    MPAnim.LockFrameToAngle(
      _animatable.animUtil,
      "cycle",
      drive.CurrentAngleRad,
      reverse: true
    );
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
  /// <remarks>
  /// The ease-in has to be fast. Its factor is what the animator weights every pose by, and it
  /// starts at zero on each <c>StartAnimation</c>, so until it climbs the machine is drawn part way
  /// back toward its unposed shape. At the vanilla default of 10 that takes a moment; at 1 it takes
  /// seconds, and every animator rebuild - every construction stage - restarts it.
  /// </remarks>
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
        EaseInSpeed = 10f,
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

    float output = OutputPerSecond;
    if (output <= 0f) {
      dsc.AppendLine(Lang.Get("ppex:mpfluidpump-info-idle"));
      return;
    }

    dsc.AppendLine(
      Lang.Get(
        "ppex:mpfluidpump-info-running",
        ExMeasure.FlowRate(output),
        ExMeasure.Pressure(PpexValues.MpPumpDeliveryPressure)
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
    // The renderer holds a reference to this block entity; leaving it registered keeps a removed
    // pump alive and still writing frames.
    (Api as ICoreClientAPI)?.Event.UnregisterRenderer(
      this,
      EnumRenderStage.Before
    );
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

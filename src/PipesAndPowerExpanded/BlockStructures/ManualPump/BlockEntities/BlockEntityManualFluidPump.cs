using System;
using ExpandedLib.Blocks.Machines;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.ManualPump.BlockEntities;

/// <summary>
/// The manual (hand-cranked) fluid pump. A no-power counterpart to the engine fluid pump, worked
/// by holding right-click on the block (or its top filler). While cranked it transfers water from
/// the fluid intake on its input line into its output line at a fixed 1 atm - a manual way to
/// start a boiler's water loop before any steam engine exists. The pump is not the source: the
/// <see cref="BlockEntityFluidIntake"/> on the input network is the generator, and each tick the
/// standing input water is moved out first, then the intake refills it.
/// </summary>
[BlockEntityRegister]
public class BlockEntityManualFluidPump : BlockEntity {
  // --- Synced run state (server-authoritative, mirrored to clients for animation/sound) ---
  /// <summary>True while a player is actively cranking the pump (holding right-click).</summary>
  private bool _pumping;

  /// <summary>True while the pump has an active intake on its input line and is moving water.</summary>
  private bool _drawingWater;

  // Server-side watchdog: world ms of the last interaction step. The stop event normally clears
  // _pumping, but if it's missed (teleport/death/disconnect mid-hold) the work tick stops the pump.
  private long _lastStepMs;
  private long _serverTickId;

  // --- Client animation + sound ---
  private BEBehaviorAnimatable? _animatable;
  private bool _animatorReady;
  private bool _animPumping;
  private long _clientTickId;
  private ILoadedSound? _grindSound;
  private ILoadedSound? _waterSound;

  /// <summary>Horizontal placement angle (north 0, west 90, south 180, east 270).</summary>
  private int Angle => ExOrientation.AngleFromSide(Block.Variant["side"]);

  /// <summary>The input (water source) connector face - south in the north orientation.</summary>
  private BlockFacing InputFace =>
    ExOrientation.RotateFacing(BlockFacing.SOUTH, Angle);

  /// <summary>The output (delivery) connector face - north in the north orientation.</summary>
  private BlockFacing OutputFace =>
    ExOrientation.RotateFacing(BlockFacing.NORTH, Angle);

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);

    if (api.Side == EnumAppSide.Server) {
      // Nobody is holding the button right after load, so ignore any persisted run state.
      _pumping = false;
      _drawingWater = false;
      _serverTickId = RegisterGameTickListener(OnServerTick, 1000);
    } else {
      _animatable = GetBehavior<BEBehaviorAnimatable>();
      InitAnimator();
      ApplyAnim(_pumping);
      _animPumping = _pumping;
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
    }
  }

  #region Interaction (driven by the block / top filler)

  /// <summary>Begins (or keeps alive) a crank started by a right-click hold on the pump.</summary>
  public void OnPumpStart() {
    _lastStepMs = Api.World.ElapsedMilliseconds;
    if (_pumping)
      return;
    _pumping = true;
    if (Api.Side == EnumAppSide.Server)
      MarkDirty();
  }

  /// <summary>Keeps the crank alive while the button is held (refreshes the watchdog).</summary>
  public void OnPumpStep() => _lastStepMs = Api.World.ElapsedMilliseconds;

  /// <summary>Ends the crank when the button is released.</summary>
  public void OnPumpStop() => StopPumping();

  private void StopPumping() {
    if (!_pumping && !_drawingWater)
      return;
    _pumping = false;
    _drawingWater = false;
    if (Api.Side == EnumAppSide.Server)
      MarkDirty();
  }

  #endregion

  #region Server work

  private void OnServerTick(float dt) {
    if (!_pumping)
      return;

    // Watchdog: covers a missed stop event (held interaction stopped refreshing the timestamp).
    if (Api.World.ElapsedMilliseconds - _lastStepMs > 1200) {
      StopPumping();
      return;
    }

    DoWork(dt);
  }

  /// <summary>
  /// Moves water from the input line to the output line: transfer the standing input water out
  /// (capped by output free capacity) first, then have the intake refill it, so the input pipe
  /// reads as a water line rather than an empty "Air" pool at broadcast time.
  /// </summary>
  private void DoWork(float dt) {
    var ba = Api.World.BlockAccessor;
    PipeNetwork? inputNet = ConnectedNetwork(InputFace);
    PipeNetwork? outputNet = ConnectedNetwork(OutputFace);

    // The intake is the generator; with none on the input line the crank turns but moves nothing.
    BlockEntityFluidIntake? intake = FindIntake(inputNet);
    bool drawing = intake != null;
    if (drawing) {
      float amount = PpexValues.ManualPumpWaterPerSecond * dt;
      float move = Math.Min(amount, OutputFreeCapacity(outputNet));
      float drawn = inputNet?.TryConsumeLiquid(move, ba) ?? 0f;
      if (drawn > 0f)
        // Hand-cranked head, independent of how fast the handle is turned.
        outputNet?.TryProduceLiquid(
          drawn,
          ExClimate.AmbientAt(this),
          PpexValues.ManualPumpDeliveryPressure,
          ba
        );
      intake!.ProduceWater(amount, ExClimate.AmbientAt(this), ba);
    }

    if (drawing != _drawingWater) {
      _drawingWater = drawing;
      MarkDirty();
    }
  }

  /// <summary>The pipe network across one of the pump's connector faces (only when a pipe there
  /// faces back), or <c>null</c>.</summary>
  private PipeNetwork? ConnectedNetwork(BlockFacing connectorFace) =>
    this.ConnectedNetwork<PipeNetwork>(connectorFace);

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

  /// <summary>Litres of water the output network can still accept.</summary>
  private static float OutputFreeCapacity(PipeNetwork? net) =>
    net == null
      ? 0f
      : net.Nodes.Count * PpexValues.LitresPerPipe - (net.State?.Volume ?? 0f);

  #endregion

  #region Client animation + sound

  private void OnClientTick(float dt) {
    if (_pumping != _animPumping) {
      _animPumping = _pumping;
      ApplyAnim(_pumping);
    }
    UpdateSounds();
  }

  /// <summary>
  /// Builds the animator from the block's shape. Leaves <see cref="_animatorReady"/> false if the
  /// shape fails to resolve, so a pose is never queued against a null animator (vanilla NREs).
  /// </summary>
  private void InitAnimator() {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    MeshData meshData = _animatable.animUtil.CreateMesh(
      Block.Code.Path,
      null,
      out Shape resolvedShape,
      null,
      new TesselationMetaData()
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
  /// Holds one animation at a time - <c>cycle</c> while cranked, <c>idle</c> otherwise. Keeping
  /// one active stops the animator mesh vanishing (and the GetBlockInfo NRE).
  /// </summary>
  private void ApplyAnim(bool running) {
    if (_animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    if (running) {
      util.StopAnimation("idle");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = "cycle",
          Code = "cycle",
          AnimationSpeed = 1f,
          EaseInSpeed = 1f,
          EaseOutSpeed = 5f,
        }.Init()
      );
    } else {
      util.StopAnimation("cycle");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = "idle",
          Code = "idle",
          AnimationSpeed = 1f,
          EaseInSpeed = 1f,
          EaseOutSpeed = 5f,
        }.Init()
      );
    }
  }

  /// <summary>
  /// Drives the two looping work sounds from the synced state: a muted iron grind whenever the
  /// pump is cranked, and a watering loop on top of it only while it is actually drawing water.
  /// </summary>
  private void UpdateSounds() {
    if (Api is not ICoreClientAPI)
      return;

    if (_pumping) {
      _grindSound ??= ExSounds.CreateLoop(
        Api,
        Pos,
        ExSounds.MetalGrinding,
        volume: 0.35f,
        range: 16f
      );
      if (_grindSound?.IsPlaying == false)
        _grindSound.Start();
    } else if (_grindSound?.IsPlaying == true)
      _grindSound.Stop();

    if (_drawingWater) {
      _waterSound ??= ExSounds.CreateLoop(
        Api,
        Pos,
        ExSounds.Watering,
        volume: 1f,
        range: 16f
      );
      if (_waterSound?.IsPlaying == false)
        _waterSound.Start();
    } else if (_waterSound?.IsPlaying == true)
      _waterSound.Stop();
  }

  private void DisposeSounds() {
    _grindSound?.Stop();
    _grindSound?.Dispose();
    _grindSound = null;
    _waterSound?.Stop();
    _waterSound?.Dispose();
    _waterSound = null;
  }

  #endregion

  #region Persistence + lifecycle

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetBool("pumping", _pumping);
    tree.SetBool("drawingWater", _drawingWater);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _pumping = tree.GetBool("pumping");
    _drawingWater = tree.GetBool("drawingWater");
  }

  public override void OnBlockRemoved() {
    if (_serverTickId != 0)
      UnregisterGameTickListener(_serverTickId);
    if (_clientTickId != 0)
      UnregisterGameTickListener(_clientTickId);
    DisposeSounds();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    if (_serverTickId != 0)
      UnregisterGameTickListener(_serverTickId);
    if (_clientTickId != 0)
      UnregisterGameTickListener(_clientTickId);
    DisposeSounds();
    base.OnBlockUnloaded();
  }

  #endregion
}

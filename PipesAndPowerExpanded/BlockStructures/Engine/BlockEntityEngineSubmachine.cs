using System;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Base class for the engine sub-machines - fluid pump, air blower, MP generator. Each is placed
/// at the engine's sub-machine cell and reads the master engine's
/// <see cref="BlockEntityEngine.AvailablePower"/> to scale its output; the engine is the source
/// of truth for the cycle tempo. This base owns the generic behavior (engine discovery, per-second
/// <see cref="DoWork"/>, and the <c>idle</c>/<c>cycle</c> animator), so a new sub-machine only
/// needs an <c>Animatable</c> behavior with <c>idle</c>/<c>cycle</c> animations plus a
/// <see cref="DoWork"/> implementation.
/// </summary>
public abstract class BlockEntityEngineSubmachine : BlockEntity
{
  protected BlockNetworkModSystem? NetSystem;
  private BEBehaviorAnimatable? _animatable;
  private long _tickId;
  private bool _animatorReady;
  private bool _animRunning;
  private float _animSpeed = 1f;

  // Client-side per-stroke piston sound watch (separate fast tick from the anim mirror).
  private long _keyframeTickId;
  private float _lastCycleFrame = -1f;

  /// <summary>World position of the master engine, located on initialize.</summary>
  protected BlockPos? EnginePos;

  /// <summary>The master engine block entity, or <c>null</c> if not found. Re-resolves
  /// <see cref="EnginePos"/> on demand: the BEs initialize in arbitrary chunk-load order, so a
  /// one-shot lookup at Initialize can miss the engine (especially client-side). Lazily retrying
  /// while unresolved fixes that.</summary>
  public BlockEntityEngine? Engine
  {
    get
    {
      EnginePos ??= FindEngine();
      return
        EnginePos != null
        && Api.World.BlockAccessor.GetBlockEntity(EnginePos)
          is BlockEntityEngine e
        ? e
        : null;
    }
  }

  /// <summary>
  /// Fraction of engine power (0..1) this sub-machine can actually use this tick.
  /// The engine reads it to consume only as much steam as is demanded (no waste).
  /// </summary>
  public virtual float PowerDemand => Engine != null ? 1f : 0f;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    EnginePos = FindEngine();

    if (api.Side == EnumAppSide.Server)
    {
      NetSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
      _tickId = RegisterGameTickListener(OnServerTick, 1000);
    }
    else
    {
      _animatable = GetBehavior<BEBehaviorAnimatable>();
      InitAnimator();
      // Hold the rest pose immediately; the poll switches to cycle if the engine already runs.
      var engine = Engine;
      _animRunning = engine?.IsRunning ?? false;
      _animSpeed = engine?.AnimationSpeed ?? 1f;
      ApplyAnim(_animRunning, _animSpeed);
      _tickId = RegisterGameTickListener(OnClientAnimTick, 500);
      // Fast watch for the per-stroke piston sounds (keyframe crossings).
      _keyframeTickId = RegisterGameTickListener(OnKeyframeTick, 50);
    }
  }

  /// <summary>
  /// Fires per-stroke piston sounds as the <c>cycle</c> animation crosses its up/down keyframes.
  /// Phase-locked to the engine, so the two stroke in step.
  /// </summary>
  private void OnKeyframeTick(float dt)
  {
    if (!_animRunning || _animatable?.animUtil?.animator is not { } animator)
    {
      _lastCycleFrame = -1f;
      return;
    }

    var st = animator.GetAnimationState("cycle");
    if (st?.Animation == null)
      return;
    float frame = st.CurrentFrame;
    int total = st.Animation.QuantityFrames;
    if (total > 1)
    {
      if (_lastCycleFrame >= 0f)
        OnCycleStroke(_lastCycleFrame, frame, total);
      _lastCycleFrame = frame;
    }
  }

  /// <summary>
  /// Hook for per-stroke effects as the <c>cycle</c> advances. The base plays the shared engine
  /// piston strokes; a sub-machine with its own cylinder overrides this (see the air blower).
  /// </summary>
  protected virtual void OnCycleStroke(
    float lastFrame,
    float currentFrame,
    int totalFrames
  ) =>
    PistonCycleSounds.Fire(
      Api.World,
      Pos,
      lastFrame,
      currentFrame,
      totalFrames
    );

  /// <summary>
  /// The pipe network across one of this sub-machine's connector faces, or <c>null</c> when the
  /// adjacent pipe has no connector facing back.
  /// </summary>
  protected PipeNetwork? ConnectedNetwork(BlockFacing connectorFace) =>
    NetSystem?.GetConnectedNetworkAcross(
      Api.World.BlockAccessor,
      Pos,
      connectorFace
    ) as PipeNetwork;

  /// <summary>Locates the engine that owns this sub-machine cell (assumes aligned orientation).</summary>
  private BlockPos? FindEngine()
  {
    // Invert BlockEngine.SubmachinePos (engine + rotate(submachineOffset, bodyAngle)) to get the
    // engine cell. BodyAngle = AngleFromSide + 180 (the body frame), matched here; the offset is
    // read from this block's own JSON so it stays in step with the engine's.
    int angle =
      (ExOrientation.AngleFromSide(Block.Variant["side"]) + 180) % 360;
    Vec3i off = ExOrientation.ReadOffset(
      Block,
      "submachineOffset",
      new Vec3i(0, 0, 2)
    );
    Vec3i r = ExOrientation.RotateOffset(off, angle);
    BlockPos cand = Pos.AddCopy(-r.X, -r.Y, -r.Z);
    if (Api.World.BlockAccessor.GetBlockEntity(cand) is BlockEntityEngine)
      return cand;

    // Fallback: the engine sits two cells away along a horizontal axis.
    foreach (var f in BlockFacing.HORIZONTALS)
    {
      BlockPos p = Pos.AddCopy(f.Normali.X * 2, 0, f.Normali.Z * 2);
      if (Api.World.BlockAccessor.GetBlockEntity(p) is BlockEntityEngine)
        return p;
    }
    return null;
  }

  /// <summary>Per-second server work, scaled by <paramref name="power"/> (0..max).</summary>
  protected abstract void DoWork(float power, float dt);

  private void OnServerTick(float dt)
  {
    EnginePos ??= FindEngine();
    var engine = Engine;
    if (engine == null)
      return;
    DoWork(engine.AvailablePower, dt);
  }

  // Mirror the engine's cycle animation; re-apply on a run-state flip or meaningful speed change.
  private void OnClientAnimTick(float dt)
  {
    var engine = Engine;
    bool run = engine?.IsRunning ?? false;
    float speed = engine?.AnimationSpeed ?? 1f;
    if (run != _animRunning || (run && Math.Abs(speed - _animSpeed) > 0.05f))
    {
      _animRunning = run;
      _animSpeed = speed;
      ApplyAnim(run, speed);
    }

    OnClientStateTick(dt);
  }

  /// <summary>
  /// Client-side hook polled alongside the animation mirror (~twice a second) for state-driven
  /// effects such as a work-loop sound. The base does nothing (see the fluid pump's water loop).
  /// </summary>
  protected virtual void OnClientStateTick(float dt) { }

  /// <summary>
  /// Builds the animator from the block's shape. Leaves <see cref="_animatorReady"/> false if the
  /// shape fails to resolve, so we never pose a null animator (vanilla GetBlockInfo would NRE).
  /// </summary>
  private void InitAnimator()
  {
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
  /// Holds one animation at a time: <c>cycle</c> (at the engine's speed) while driven, <c>idle</c>
  /// otherwise. Keeping one active stops the animator mesh vanishing (and the GetBlockInfo NRE).
  /// </summary>
  protected virtual void ApplyAnim(bool running, float speed)
  {
    if (_animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    if (running)
    {
      util.StopAnimation("idle");
      util.StartAnimation(
        new AnimationMetaData
        {
          Animation = "cycle",
          Code = "cycle",
          AnimationSpeed = speed,
          EaseInSpeed = 3f,
          EaseOutSpeed = 3f,
        }.Init()
      );
      PhaseLockToEngine();
    }
    else
    {
      util.StopAnimation("cycle");
      util.StartAnimation(
        new AnimationMetaData
        {
          Animation = "idle",
          Code = "idle",
          AnimationSpeed = 1f,
          EaseInSpeed = 3f,
          EaseOutSpeed = 3f,
        }.Init()
      );
    }
  }

  /// <summary>
  /// Called by the master engine (from its own pose change) to start/stop this sub-machine's
  /// cycle in the same client frame as the engine's, so the two never start out of phase.
  /// </summary>
  public void SyncAnimation(bool running, float speed)
  {
    if (Api is not ICoreClientAPI)
      return;
    _animRunning = running;
    _animSpeed = speed;
    ApplyAnim(running, speed);
  }

  /// <summary>
  /// Snaps our just-started <c>cycle</c> to the engine's current progress so the pistons stroke
  /// in lockstep regardless of when each animation began.
  /// </summary>
  private void PhaseLockToEngine()
  {
    if (
      _animatable?.animUtil?.animator is not { } animator
      || Engine is not { } engine
    )
      return;
    var st = animator.GetAnimationState("cycle");
    if (st?.Animation == null || st.Animation.QuantityFrames <= 1)
      return;
    st.CurrentFrame =
      engine.CycleAnimProgress * (st.Animation.QuantityFrames - 1);
  }

  /// <summary>
  /// Fired when the engine snaps this sub-machine to its matching orientation via
  /// <c>ExchangeBlock</c> (engine placed onto an existing sub-machine), keeping this BE alive.
  /// Re-resolve the engine and rebind the animator to the new orientation, then restore the pose.
  /// </summary>
  public override void OnExchanged(Block block)
  {
    base.OnExchanged(block);
    EnginePos = null;
    if (Api is ICoreClientAPI)
    {
      _animatorReady = false;
      InitAnimator();
      ApplyAnim(_animRunning, _animSpeed);
    }
  }

  /// <summary>Left face in north orientation, rotated to this block's placement.</summary>
  protected BlockFacing LeftFace =>
    ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide(Block.Variant["side"])
    );

  public override void OnBlockRemoved()
  {
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
    if (_keyframeTickId != 0)
      UnregisterGameTickListener(_keyframeTickId);
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_tickId != 0)
      UnregisterGameTickListener(_tickId);
    if (_keyframeTickId != 0)
      UnregisterGameTickListener(_keyframeTickId);
    base.OnBlockUnloaded();
  }
}

using System;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Base class (and the designation the engine looks up) for the engine sub-machines —
/// fluid pump, air blower, MP generator. Each is placed at the engine's sub-machine cell
/// and reads the master engine's <see cref="BlockEntityEngine.AvailablePower"/> to scale
/// its output; the engine, not the sub-machine, is the source of truth for the cycle tempo.
/// <para>
/// This base owns all the generic sub-machine behavior: engine discovery, per-second server
/// work (<see cref="DoWork"/>), and the animator — every sub-machine holds an <c>idle</c>
/// animation at rest and switches to the <c>cycle</c> animation (synced to the engine's speed)
/// while driven. A sub-machine block therefore only needs an <c>Animatable</c> behavior plus
/// <c>idle</c>/<c>cycle</c> animations in its shape; extend this and implement
/// <see cref="DoWork"/> to add a new one.
/// </para>
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

  /// <summary>The master engine block entity (Cornish or Watt), or <c>null</c> if not found.
  /// Re-resolves <see cref="EnginePos"/> on demand: the engine and sub-machine BEs initialize in
  /// arbitrary chunk-load order, so a one-shot lookup at Initialize can miss the engine (especially
  /// on the client, where nothing else re-runs FindEngine) — which would leave the MP generator's
  /// render-driven engine animation dead. Lazily retrying while unresolved fixes that.</summary>
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
      // Hold the rest pose immediately; the poll below switches to the cycle if the
      // engine is already running when this sub-machine loads in.
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
  /// Fires the per-stroke piston sounds as this sub-machine's <c>cycle</c> animation crosses
  /// its up/down keyframes, mirroring the engine. The cycle is phase-locked to the engine, so
  /// the two machines stroke in step.
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
  /// Reacts to the <c>cycle</c> animation advancing from <paramref name="lastFrame"/> to
  /// <paramref name="currentFrame"/> this client tick — the hook for per-stroke piston sounds
  /// and effects. The base plays the shared engine-piston stroke sounds (the fluid pump uses
  /// them as-is); a sub-machine with its own cylinder overrides this to fire its own keyframe
  /// effects (see the air blower's bellows/clang + ambient-air inhale).
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
  /// The pipe network connected across one of this sub-machine's connector faces, or
  /// <c>null</c> when the adjacent pipe has no connector facing back (so it is not actually
  /// plumbed in). Sub-machines only ever touch networks they are genuinely connected to.
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
    // The engine places the sub-machine at engine + rotate(submachineOffset, engineBodyAngle)
    // (see BlockEngine.SubmachinePos); invert that to get back to the engine cell. BodyAngle is
    // the engine's AngleFromSide + 180 — the +180 "body" frame the mesh, fillers and connectors
    // all live in — so match it here. The offset is read from this block's own JSON attribute so
    // it stays in step with the engine's.
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

  // Mirror the engine's cycle animation — the engine sets the tempo. Re-apply on a
  // run-state flip or a meaningful speed change so the sub-machine stays in step.
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
  }

  /// <summary>
  /// Builds the animator from the block's shape so the sub-machine renders its animated
  /// mesh. Client-side; leaves <see cref="_animatorReady"/> false if the shape fails to
  /// resolve, so we never queue a pose against a null animator (vanilla GetBlockInfo would
  /// then NRE iterating the animator under extended debug info).
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
  /// Holds exactly one animation at a time: <c>cycle</c> (at the engine's speed) while
  /// driven, <c>idle</c> otherwise. Keeping one always active stops the animator-rendered
  /// mesh from vanishing (and the debug GetBlockInfo NRE) when nothing is playing.
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
  /// Snaps our just-started <c>cycle</c> animation to the engine's current cycle progress so
  /// the piston strokes in lockstep with the engine regardless of when each animation began.
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
  /// <c>ExchangeBlock</c> (case 1: engine placed onto an already-present sub-machine), which
  /// keeps this block entity alive. Re-resolve the engine (our offset back to it now points a
  /// different way) and rebind the animator to the new orientation's renderer rotation, then
  /// restore the current pose. Fires on both sides; only the client has an animator.
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

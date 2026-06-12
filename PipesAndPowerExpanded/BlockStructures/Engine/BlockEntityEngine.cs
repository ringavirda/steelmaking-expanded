using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Shared base for the steam engines — a single mega-block that renders across its
/// footprint and is raised in place via the vanilla <c>RightClickConstructable</c>
/// behavior. Like the other mega-blocks it is drawn through the animator (RCC
/// suppresses the default mesh), and the surrounding cells are reserved with
/// invisible structure fillers.
/// <para>
/// The block at the sub-machine cell <c>(0,0,2)</c> (relative to the master, rotated
/// by orientation) decides the pose: an <c>mpgenerator</c> uses the <c>idlemp</c> /
/// <c>cyclemp</c> animations, an air blower / fluid pump (or nothing) uses the normal
/// <c>idle</c> / <c>cyclepump</c> animations. The cycle plays while the engine drives
/// its sub-machine.
/// </para>
/// <para>
/// Per-variant stats (pressures, power, steam draw) are supplied through the virtual
/// hooks below.
/// </para>
/// </summary>
public abstract class BlockEntityEngine : BlockEntity
{
  private BEBehaviorAnimatable? _animatable;
  private BEBehaviorRightClickConstructable? _rcc;
  private bool _animatorReady;

  // The sub-machine sits two cells away (not a neighbour of the master), so its
  // placement/removal never reaches us through OnNeighbourBlockChange. A light
  // client poll re-poses the engine when the attached machine type changes.
  private long _submachineWatchId;
  private bool _lastMp;

  // Client-side stroke-sound + over-pressure-steam watch.
  private long _engineClientTickId;
  private float _lastCycleFrame = -1f;
  private long _overSteamMs;

  // MP-generator variant: the attached generator drives our cycle frame-by-frame from its axle
  // (see DriveMpCycleFrame). This is the client-side "axle is turning" flag it last pushed, which
  // ApplyPose reads to choose cyclemp vs idlemp for the MP case.
  private bool _mpTurning;

  // Forward-accumulated cyclemp frame + last axle angle the generator pushed, so we advance the
  // cycle by the axle's rotation MAGNITUDE each frame (always forward at the axle's rate). See
  // DriveMpCycleFrame for why the signed angle can't be used directly.
  private float _mpCycleFrame;
  private float _lastDriveAngle;

  // Constant low planetary-gear hum from the gear housing while the engine runs (client only).
  private ILoadedSound? _gearSound;

  /// <summary>Set true while the engine is driving its sub-machine (cycle animation).</summary>
  private bool _running;

  private long _engineTickId;
  private BlockNetworkModSystem? _netSystem;

  #region Per-variant stats

  /// <summary>Nominal maximum power (display reference; actual delivered power is <see cref="RunPower"/>).</summary>
  protected abstract float MaxPowerValue { get; }

  /// <summary>Inlet steam pressure (atm) at/above which the engine runs.</summary>
  protected abstract float EngagePressure { get; }

  /// <summary>Inlet pressure (atm) above which the engine wears toward a break.</summary>
  protected abstract float BreakPressure { get; }

  /// <summary>Steam (L/s) the engine draws while running at its current setting.</summary>
  protected abstract float RunSteamRate { get; }

  /// <summary>Power the engine delivers while running at its current setting.</summary>
  protected abstract float RunPower { get; }

  /// <summary>Hot condensed water (L/s) the engine spits out its outlet while running at its current setting.</summary>
  protected abstract float RunWaterOutput { get; }

  /// <summary>Particles vented out the cylinder top on each power stroke. Overridden per variant
  /// to scale with the running setting (the Cornish throttle); 0 suppresses the cylinder puff.</summary>
  protected virtual int CylinderSteamPuffCount => 2;

  /// <summary>Volume multiplier for the engine's running sounds (piston strokes + gear hum),
  /// 1 = unchanged. Overridden per variant so the Cornish engine roars louder when overclocked.</summary>
  protected virtual float SoundVolumeFactor => 1f;

  /// <summary>Pitch multiplier for the gear hum, 1 = unchanged. Below 1 drops it to a heavier,
  /// more violent growl when the engine is driven hard.</summary>
  protected virtual float SoundPitchFactor => 1f;

  #endregion

  #region Break / repair

  /// <summary>Seconds the engine has run above its band (drives the break; reset when back in band).</summary>
  private float _overPressureSeconds;

  /// <summary>True once the engine has burst from sustained over-pressure; it can't run until repaired.</summary>
  public bool IsBroken { get; private set; }

  /// <summary>Seconds of over-pressure left before the engine breaks (for the HUD warning).</summary>
  public float OverPressureRemaining =>
    Math.Max(0f, PpexValues.EngineOverPressureSeconds - _overPressureSeconds);

  /// <summary>Clears the broken state (called by the block's wrench repair).</summary>
  public void Repair()
  {
    if (!IsBroken)
      return;
    IsBroken = false;
    _overPressureSeconds = 0f;
    MarkDirty(true);
  }

  /// <summary>Bursts the engine: it stops and stays inert until repaired.</summary>
  private void Break()
  {
    IsBroken = true;
    AvailablePower = 0f;
    _running = false;
    // Clear the over-pressure timer: a broken engine has already burst, so it must stop venting
    // the over-pressure warning plume (the client steam vent below is gated on this being > 0, and
    // the broken engine's tick returns early without ever resetting it otherwise).
    _overPressureSeconds = 0f;
    if (Api is { Side: EnumAppSide.Server })
    {
      // A burst engine erupts in a thick cloud of steam and gives a muffled explosion
      // (server-spawned particles replicate to nearby clients).
      ExParticles.SteamPlume(Api.World, Pos, 120);
      // …topped by a sooty smoke blast out of the cylinder top, where the spent-steam puffs
      // normally vent — the visible mark of the cylinder letting go.
      Vec3d vent =
        EngineBlock?.CylinderVentPos(Pos).AddCopy(new Vec3d(0, -0.5, 0))
        ?? Pos.ToVec3d().Add(0.5, 0.5, 0.5);
      ExParticles.SmokeCloud(Api.World, vent, 80);
      ExSounds.PlayAt(
        Api.World,
        Pos,
        ExSounds.MediumExplosion,
        null,
        randomizePitch: false,
        range: 24f,
        volume: 0.5f
      );
    }
    MarkDirty(true);
  }

  #endregion

  /// <summary>True once the player has finished the construction stages.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Available mechanical power (0..<see cref="MaxPower"/>), from inlet steam pressure.</summary>
  public float AvailablePower { get; private set; }

  /// <summary>Inlet steam pressure (atm) read last tick. Sub-machines set their output
  /// pressure to this times <see cref="PpexValues.SteamEngineEfficiency"/>.</summary>
  public float InletPressure { get; private set; }

  /// <summary>Maximum power this engine can deliver to its sub-machine at rated pressure.</summary>
  public float MaxPower => MaxPowerValue;

  #region MP generator drive

  /// <summary>
  /// MP consumer load (resistance) the attached generator can hold at <see cref="PpexValues.MpRatedSpeed"/>
  /// when this engine is at FULL power — four active helve hammers for the Watt (0.125 each). The
  /// network slows once the load passes this, and the engine overstresses and stops once it passes
  /// double this. Constant (uses <see cref="MaxPower"/>, not the current output) so the stall check
  /// can't latch off when the engine has already stopped. Scales with the engine's power tier.
  /// </summary>
  public float MpRatedLoad => MaxPower * PpexValues.MpLoadPerEnginePower;

  /// <summary>
  /// Constant mechanical-power budget the generator delivers to the MP network at the engine's
  /// CURRENT output (so a steam-starved engine drives proportionally less). The generator runs a
  /// constant-power source — torque = budget / speed — so the network settles at
  /// <c>speed = budget / load</c>, i.e. the rated speed at the rated load and slower under more.
  /// </summary>
  public float MpPowerBudget =>
    AvailablePower * PpexValues.MpLoadPerEnginePower * PpexValues.MpRatedSpeed;

  /// <summary>True when the MP <paramref name="load"/> has grown past what keeps the network above
  /// half the rated speed — the engine can no longer drive it and stalls out until load is shed.</summary>
  public bool IsMpOverstressed(float load) => load > 2f * MpRatedLoad;

  #endregion

  /// <summary>Where the inlet steam pressure sits against the operating band, as a lang-key
  /// fragment: <c>over</c> (above break — wearing toward a burst), <c>nominal</c> (in band),
  /// <c>under</c> (some steam but below the engage pressure), or <c>idle</c> (no steam).</summary>
  private string ClockState =>
    InletPressure > BreakPressure ? "over"
    : InletPressure >= EngagePressure ? "nominal"
    : InletPressure > 0.01f ? "under"
    : "idle";

  public float AnimationSpeed { get; private set; } = 1f;

  /// <summary>True while the engine is running (sub-machine being driven).</summary>
  public bool IsRunning => _running;

  private BlockEngine? EngineBlock => Block as BlockEngine;

  /// <summary>
  /// The pipe network connected to the engine across one of its own connector faces, or
  /// <c>null</c> when the adjacent pipe has no connector facing back at the engine (so it is
  /// not actually plumbed in). Used for the steam inlet and the condensed-water outlet.
  /// </summary>
  private PipeNetwork? ConnectedNetwork(BlockFacing connectorFace) =>
    _netSystem?.GetConnectedNetworkAcross(
      Api.World.BlockAccessor,
      Pos,
      connectorFace
    ) as PipeNetwork;

  /// <summary>The attached sub-machine block entity at the engine's sub-machine cell, if any.</summary>
  public BlockEntityEngineSubmachine? SubmachineBE =>
    EngineBlock != null
      ? Api.World.BlockAccessor.GetBlockEntity(EngineBlock.SubmachinePos(Pos))
        as BlockEntityEngineSubmachine
      : null;

  #region Lifecycle

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();
    _rcc = GetBehavior<BEBehaviorRightClickConstructable>();

    if (api is ICoreClientAPI && _animatable != null)
    {
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;

      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      _lastMp = IsMpGenerator();
      ApplyPose();

      _submachineWatchId = RegisterGameTickListener(OnSubmachineWatch, 500);
      // Fast client tick: per-stroke piston sounds (keyframe crossings) + cylinder steam
      // while the engine is straining over pressure.
      _engineClientTickId = RegisterGameTickListener(OnEngineClientTick, 50);
    }

    if (api.Side == EnumAppSide.Server)
    {
      _netSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
      _engineTickId = RegisterGameTickListener(OnEngineTick, 1000);
    }
  }

  #region Power

  private void OnEngineTick(float dt)
  {
    if (!IsConstructed || EngineBlock == null)
      return;

    var ba = Api.World.BlockAccessor;

    // A broken engine is inert until repaired.
    if (IsBroken)
    {
      AvailablePower = 0f;
      if (_running)
      {
        _running = false;
        MarkDirty(true);
      }
      return;
    }

    var inlet = ConnectedNetwork(EngineBlock.SteamInletFace);
    float pressure =
      inlet?.State?.MediumType == "Steam" ? inlet.State.Pressure : 0f;
    InletPressure = pressure;

    // Over-pressure damage: running the engine above its band wears it out; sustained
    // long enough it bursts and must be repaired. Back inside the band, it recovers.
    if (pressure > BreakPressure)
    {
      _overPressureSeconds += dt;
      MarkDirty(true);
      if (_overPressureSeconds >= PpexValues.EngineOverPressureSeconds)
      {
        Break();
        return;
      }
    }
    else if (_overPressureSeconds > 0f)
    {
      _overPressureSeconds = 0f;
      MarkDirty(true);
    }

    // Fixed steam draw while engaged — the inlet pressure only gates on/off (and the
    // over-pressure break above). Power scales with the steam the network can actually
    // supply, so a starved line yields proportionally less power.
    float demand = SubmachineBE?.PowerDemand ?? 0f;
    bool engaged = pressure >= EngagePressure && demand > 0f;
    float power = 0f;

    if (engaged && inlet != null)
    {
      float want = RunSteamRate * demand * dt;
      float used = inlet.TryConsumeGas(want, ba);
      float frac = want > 0f ? used / want : 0f;
      power = RunPower * demand * frac;
      // The spent steam leaves as hot condensed water on the east outlet. The amount is
      // a fixed per-engine rate (not the raw steam volume), scaled by how hard the engine
      // is actually running, so the numbers stay clean for the water loop.
      float waterOut = RunWaterOutput * demand * frac * dt;
      if (waterOut > 0f)
        OutputCondensate(waterOut, ba);
    }

    AvailablePower = power;
    bool run = power > 0.001f;

    float newSpeed = run ? 0.5f + power : 1f;
    if (run != _running || Math.Abs(newSpeed - AnimationSpeed) > 0.05f)
    {
      AnimationSpeed = newSpeed;
      _running = run;
      MarkDirty(true); // sync running + speed to clients for the cycle animation
    }
  }

  /// <summary>
  /// Sends the engine's condensed water out its outlet. If a pipe network is connected
  /// and has room it goes into the water pool (at no pressure — only the pump pressurises
  /// water); otherwise the water is simply discarded and a splash particle is played, so
  /// an engine whose outlet isn't piped just spills on the ground.
  /// </summary>
  private void OutputCondensate(float amount, IBlockAccessor ba)
  {
    var outNet = ConnectedNetwork(EngineBlock!.WaterOutletFace);
    bool piped = outNet?.TryProduceLiquid(amount, 90f, 0f, ba) == true;
    if (!piped)
      SpawnWaterSpill();
  }

  /// <summary>Water jets out of the outlet (and an occasional splash) when the condensate has nowhere to go.</summary>
  private void SpawnWaterSpill()
  {
    if (Api is { Side: EnumAppSide.Server })
    {
      ExParticles.WaterJet(Api.World, Pos, EngineBlock!.WaterOutletFace);
      ExSounds.SplashSound(Api.World, Pos);
    }
  }

  #endregion

  /// <summary>Per-variant animator cache key (also the shape selector); unique per block code + side.</summary>
  protected virtual string AnimCacheKey => Block.Code.Path;

  // Re-pose when the sub-machine attached at (0,0,2) changes type (mpgenerator
  // vs blower/pump), which switches the engine between its mp and pump animations.
  private void OnSubmachineWatch(float dt)
  {
    bool mp = IsMpGenerator();
    if (mp == _lastMp)
      return;
    _lastMp = mp;
    ApplyPose();
  }

  /// <summary>Shared teardown for removal and unload (the two paths are identical).</summary>
  private void Cleanup()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    if (_submachineWatchId != 0)
      UnregisterGameTickListener(_submachineWatchId);
    if (_engineClientTickId != 0)
      UnregisterGameTickListener(_engineClientTickId);
    if (_engineTickId != 0)
      UnregisterGameTickListener(_engineTickId);
    DisposeGearHum();
  }

  public override void OnBlockRemoved()
  {
    Cleanup();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    Cleanup();
    base.OnBlockUnloaded();
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("running", _running);
    tree.SetFloat("animSpeed", AnimationSpeed);
    tree.SetFloat("availPower", AvailablePower);
    tree.SetFloat("inletPressure", InletPressure);
    tree.SetBool("broken", IsBroken);
    tree.SetFloat("overPressure", _overPressureSeconds);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    bool wasRunning = _running;
    bool wasBroken = IsBroken;
    float wasSpeed = AnimationSpeed;
    _running = tree.GetBool("running");
    AnimationSpeed = tree.GetFloat("animSpeed", 1f);
    AvailablePower = tree.GetFloat("availPower");
    InletPressure = tree.GetFloat("inletPressure");
    IsBroken = tree.GetBool("broken");
    _overPressureSeconds = tree.GetFloat("overPressure");
    if (Api is ICoreClientAPI && _animatorReady)
    {
      // Breaking/repairing swaps the rendered mesh (piston subtree on/off).
      if (wasBroken != IsBroken)
      {
        RebuildAnimator(_rcc?.shape?.SelectiveElements);
        ApplyPose();
      }
      // Re-pose on the client when the running state OR speed arrives so the cycle plays at
      // the right tempo — and so the sub-machine is re-synced to match (see ApplyPose).
      else if (
        wasRunning != _running
        || Math.Abs(wasSpeed - AnimationSpeed) > 0.05f
      )
        ApplyPose();
    }
  }

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  )
  {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed)
      return;

    if (IsBroken)
    {
      dsc.AppendLine(Lang.Get("ppex:engine-info-broken"));
      return;
    }

    // How the inlet steam pressure sits against the operating band, plus the steam it draws
    // while running — instead of the raw internal power figure.
    dsc.AppendLine(Lang.Get("ppex:engine-info-clock-" + ClockState));
    if (IsRunning)
      dsc.AppendLine(Lang.Get("ppex:engine-info-steam", RunSteamRate));
    if (_overPressureSeconds > 0f)
      dsc.AppendLine(
        Lang.Get("ppex:engine-info-overpressure", OverPressureRemaining)
      );
  }

  private void OnConstructShapeChanged(CompositeShape cs)
  {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyPose();
  }

  private void RebuildAnimator(string[]? selectiveElements)
  {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    // A broken engine renders without its piston/cylinder subtree (it has burst off);
    // a distinct cache key keeps the broken and intact meshes from colliding.
    bool broken = IsBroken;
    string[]? elements = broken
      ? GetBrokenSelectiveElements()
      : selectiveElements;
    string cacheKey = broken ? AnimCacheKey + "-broken" : AnimCacheKey;

    MeshData meshData = _animatable.animUtil.CreateMesh(
      cacheKey,
      null,
      out Shape resolvedShape,
      null,
      new TesselationMetaData { SelectiveElements = elements }
    );

    _animatable.animUtil.InitializeAnimator(
      cacheKey,
      meshData,
      resolvedShape,
      new Vec3f(0, Block.Shape.rotateY, 0)
    );
    // CreateMesh returns a null shape when the block's shape asset fails to
    // resolve, in which case InitializeAnimator leaves animUtil.animator null.
    // Only mark ready when the animator truly exists, so StartAnimation never
    // queues a pose against a null animator (vanilla GetBlockInfo would then NRE
    // iterating activeAnimationsByAnimCode under extendedDebugInfo).
    _animatorReady = _animatable.animUtil.animator != null;
  }

  /// <summary>Element whose subtree is hidden while the engine is broken (the piston + part of the cylinder).</summary>
  protected virtual string[] BrokenHiddenElements => ["Cube21", "Piston"];

  private string[]? _brokenSelectiveElements;

  /// <summary>
  /// Selective-element list that renders the whole engine except the
  /// <see cref="BrokenHiddenElements"/> subtree — built by walking the block's shape so
  /// it stays correct if elements move (only the named subtree is dropped). Cached.
  /// </summary>
  private string[]? GetBrokenSelectiveElements()
  {
    if (_brokenSelectiveElements != null)
      return _brokenSelectiveElements;
    if (Api is not ICoreClientAPI capi || Block.Shape?.Base == null)
      return null;

    AssetLocation loc = Block
      .Shape.Base.Clone()
      .WithPathPrefixOnce("shapes/")
      .WithPathAppendixOnce(".json");
    Shape? shape = capi.Assets.TryGet(loc)?.ToObject<Shape>();
    if (shape?.Elements == null)
      return null;

    var list = new List<string>();
    foreach (var root in shape.Elements)
      if (root.Name != null)
        CollectVisible(root, root.Name, list);
    _brokenSelectiveElements = [.. list];
    return _brokenSelectiveElements;
  }

  // Builds the SelectiveElements list that renders the whole shape except the hidden
  // subtrees. The tesselator matches per path-segment (SelectiveMatch): an *exact* element
  // name renders only that element and DROPS its children, while "<path>/*" renders the
  // element plus its entire subtree, and a "<path>/..." prefix renders the element and
  // passes the remainder down. So we emit a single "<path>/*" for each maximal clean
  // subtree; an ancestor of a hidden element is never named on its own (its own cube still
  // renders because the deeper "<path>/.../*" entries name it as a prefix) — we just recurse
  // into its non-hidden children, which excludes the hidden subtree.
  private void CollectVisible(
    ShapeElement el,
    string path,
    List<string> outList
  )
  {
    if (BrokenHiddenElements.Any(broken => broken == el.Name))
      return;
    if (!BrokenHiddenElements.Any(broken => SubtreeContains(el, broken)))
    {
      outList.Add(path + "/*");
      return;
    }
    if (el.Children != null)
      foreach (var c in el.Children)
        if (c.Name != null)
          CollectVisible(c, path + "/" + c.Name, outList);
  }

  private static bool SubtreeContains(ShapeElement el, string name)
  {
    if (el.Name == name)
      return true;
    if (el.Children != null)
      foreach (var c in el.Children)
        if (SubtreeContains(c, name))
          return true;
    return false;
  }

  #endregion

  #region Pose

  /// <summary>
  /// True when the sub-machine attached at the engine's sub-machine cell is an
  /// MP generator (which uses the alternate <c>idlemp</c>/<c>cyclemp</c> animations).
  /// </summary>
  private bool IsMpGenerator()
  {
    if (EngineBlock == null)
      return false;
    BlockPos cell = EngineBlock.SubmachinePos(Pos);
    string path = Api.World.BlockAccessor.GetBlock(cell).Code?.Path ?? "";
    return path.Contains("mpgenerator");
  }

  /// <summary>Refreshes the engine pose; call after the running state or sub-machine changes.</summary>
  public void RefreshPose() => ApplyPose();

  /// <summary>Sets whether the engine is actively running (drives the cycle animation).</summary>
  public void SetRunning(bool running)
  {
    if (_running == running)
      return;
    _running = running;
    ApplyPose();
  }

  /// <summary>
  /// The run-state + cycle speed that should drive the rendered cycle animation. For the MP
  /// generator the visible motion is the generator's own axle — an MP network with its own
  /// inertia — so its cycle is locked frame-for-frame to the axle's rotation by the generator
  /// itself (see <see cref="DriveMpCycleFrame"/>), and keeps turning while the flywheel coasts
  /// after the steam is cut. The speed returned here is only nominal (the per-frame frame drive
  /// dictates the actual position); we just report whether the axle is turning. Every other
  /// sub-machine is driven directly by the engine's steam power (the server-synced
  /// <see cref="IsRunning"/> / <see cref="AnimationSpeed"/>).
  /// </summary>
  private (bool running, float speed) CyclePose()
  {
    if (IsMpGenerator())
      return (_mpTurning, 1f);
    return (_running, AnimationSpeed);
  }

  /// <summary>
  /// Drives the engine's <c>cyclemp</c> animation from the attached MP generator's axle, the same
  /// way vanilla drives its mechanically-powered animations: one axle revolution maps to one full
  /// cycle, so the engine's beam/piston motion stays locked to the visible spinning axle at any
  /// speed (no speed-approximation drift), and keeps cycling while the flywheel coasts. Pushed every
  /// render frame by <see cref="BlockEntityEngineMpGenerator"/>; <paramref name="angleRad"/> is the
  /// axle's current render angle (0..2π) and <paramref name="turning"/> whether the network is
  /// moving. We accumulate the rotation MAGNITUDE rather than tracking the signed angle — see the
  /// body for why (the generator axle is a fixed-direction source).
  /// </summary>
  public void DriveMpCycleFrame(bool turning, float angleRad)
  {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    // Switch idlemp <-> cyclemp only on a state flip (ApplyPose reads _mpTurning).
    if (turning != _mpTurning)
    {
      _mpTurning = turning;
      ApplyPose();
      // Reset the baseline so the first frame after (re)start doesn't jump by a stale delta.
      _lastDriveAngle = angleRad;
    }
    if (!turning)
      return;

    var st = _animatable.animUtil.animator?.GetAnimationState("cyclemp");
    if (st?.Animation == null)
      return;
    int total = st.Animation.QuantityFrames;

    // Advance the cycle FORWARD by the magnitude of the axle's rotation this frame. The generator's
    // axle is a fixed-direction source — it always spins the same visible way regardless of network
    // topology (an angled gear flips the network's propagation direction, hence the SIGN of AngleRad,
    // but AxisSign keeps the rendered axle turning the same way). Using the signed angle would play
    // this reciprocating cycle backwards (looks inverted) whenever propagation flips, even though the
    // axle's visible spin hasn't changed. The magnitude keeps the rate exact and the motion forward.
    float delta = Math.Abs(
      GameMath.AngleRadDistance(_lastDriveAngle, angleRad)
    );
    _lastDriveAngle = angleRad;
    _mpCycleFrame = GameMath.Mod(
      _mpCycleFrame + delta / GameMath.TWOPI * total,
      total
    );
    st.CurrentFrame = _mpCycleFrame;
  }

  private void ApplyPose()
  {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    util.StopAnimation("idlepump");
    util.StopAnimation("idlemp");
    util.StopAnimation("cyclepump");
    util.StopAnimation("cyclemp");

    bool mp = IsMpGenerator();
    var (run, speed) = CyclePose();
    string code = run
      ? (mp ? "cyclemp" : "cyclepump")
      : (mp ? "idlemp" : "idlepump");

    util.StartAnimation(
      new AnimationMetaData
      {
        Animation = code,
        Code = code,
        AnimationSpeed = run ? speed : 1f,
        EaseInSpeed = 3f,
        EaseOutSpeed = 3f,
      }.Init()
    );

    // Drive the attached sub-machine's cycle from the same call so the two start together
    // (and at the same speed); the sub-machine then phase-locks to us via CycleAnimProgress.
    // The MP generator owns no idle/cycle animator (its motion is the axle), so this is a
    // no-op there — its axle is instead what drives our pose above.
    SubmachineBE?.SyncAnimation(run, speed);
  }

  /// <summary>
  /// Progress (0..1) through the engine's currently-running cycle animation, read by the
  /// attached sub-machine to phase-lock its own cycle. 0 when not animating client-side.
  /// </summary>
  public float CycleAnimProgress
  {
    get
    {
      var (frame, total) = ReadCycleFrame();
      return total > 1 ? frame / (total - 1) : 0f;
    }
  }

  /// <summary>Current frame + total frames of the engine's running cycle animation (client-side).</summary>
  private (float frame, int total) ReadCycleFrame()
  {
    if (
      Api is not ICoreClientAPI
      || _animatable?.animUtil?.animator is not { } animator
    )
      return (0f, 0);
    string code = IsMpGenerator() ? "cyclemp" : "cyclepump";
    var st = animator.GetAnimationState(code);
    if (st?.Animation == null)
      return (0f, 0);
    return (st.CurrentFrame, st.Animation.QuantityFrames);
  }

  /// <summary>
  /// Fast client tick: fires the per-stroke piston sounds as the cycle animation crosses its
  /// up/down keyframes, and vents cylinder steam while the engine runs above its break
  /// pressure (<see cref="_overPressureSeconds"/> is synced from the server tick).
  /// </summary>
  private void OnEngineClientTick(float dt)
  {
    // For the MP generator the cycle frame is driven directly by the generator's axle each render
    // frame (see DriveMpCycleFrame); here we only read the resulting run-state for the gear hum and
    // the per-stroke sounds below.
    var (running, _) = CyclePose();

    if (!running)
    {
      _lastCycleFrame = -1f;
      StopGearHum();
    }
    else
    {
      StartGearHum();

      var (frame, total) = ReadCycleFrame();
      if (total > 1)
      {
        if (_lastCycleFrame >= 0f)
        {
          PistonCycleSounds.Fire(
            Api.World,
            Pos,
            _lastCycleFrame,
            frame,
            total,
            SoundVolumeFactor
          );
          // A steam puff out of the cylinder top at the top of each power stroke — but only
          // while the engine is actually making power (_running), not merely coasting on the
          // MP flywheel. This is the visible sign that it's producing power. The count is a
          // per-variant hook so the Cornish engine can scale it with its throttle (and a 0
          // count suppresses the puff entirely).
          if (
            _running
            && CylinderSteamPuffCount > 0
            && PistonCycleSounds.CrossedUpStroke(_lastCycleFrame, frame, total)
          )
            ExParticles.SteamPuff(
              Api.World,
              EngineBlock!.CylinderVentPos(Pos),
              CylinderSteamPuffCount
            );
        }
        _lastCycleFrame = frame;
      }
    }

    // While straining over its break pressure, vent a constant hard plume out of the cylinder
    // top (a visible "back off" warning), independent of the stroke timing. Throttled so the
    // per-50ms tick doesn't flood particles.
    if (
      _overPressureSeconds > 0f
      && Api.World.ElapsedMilliseconds - _overSteamMs >= 200
    )
    {
      _overSteamMs = Api.World.ElapsedMilliseconds;
      ExParticles.SteamPuff(Api.World, EngineBlock!.CylinderVentPos(Pos), 3);
    }
  }

  /// <summary>Lazily creates and starts the constant low gear hum at the gear housing.</summary>
  private void StartGearHum()
  {
    _gearSound ??= ExSounds.CreateLoop(
      Api,
      EngineBlock?.GearHousingPos(Pos) ?? Pos,
      ExSounds.PlanetaryGears,
      0.5f,
      16f,
      // Pitched down for a low, heavy planetary-gear hum (vanilla plays this sound at 0.7 for its
      // generator). Audible as a background layer under the per-stroke piston sounds.
      0.65f
    );
    if (_gearSound is { IsPlaying: false })
      _gearSound.Start();
    // Apply the current sound profile live (called every running tick) so a throttle change is
    // heard on the already-looping hum without restarting it.
    _gearSound?.SetVolume(0.5f * SoundVolumeFactor);
    _gearSound?.SetPitch(0.65f * SoundPitchFactor);
  }

  /// <summary>Stops the gear hum (kept allocated so it can resume when the engine restarts).</summary>
  private void StopGearHum()
  {
    if (_gearSound is { IsPlaying: true })
      _gearSound.Stop();
  }

  /// <summary>Stops and releases the gear hum on block removal/unload.</summary>
  private void DisposeGearHum()
  {
    _gearSound?.Stop();
    _gearSound?.Dispose();
    _gearSound = null;
  }

  #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

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
public abstract class BlockEntityEngineBase : BlockEntity
{
  // Sub-machine cell, north orientation; rotated per placement.
  private static readonly Vec3i SubmachineLocal = new(0, 0, 2);

  private BEBehaviorAnimatable? _animatable;
  private BEBehaviorRightClickConstructable? _rcc;
  private bool _animatorReady;

  // The sub-machine sits two cells away (not a neighbour of the master), so its
  // placement/removal never reaches us through OnNeighbourBlockChange. A light
  // client poll re-poses the engine when the attached machine type changes.
  private long _submachineWatchId;
  private bool _lastMp;

  /// <summary>Set true while the engine is driving its sub-machine (cycle animation).</summary>
  private bool _running;

  private long _engineTickId;
  private BlockNetworkModSystem? _netSystem;

  #region Per-variant stats

  /// <summary>Nominal maximum power at rated pressure (an overclocked engine may exceed it).</summary>
  protected abstract float MaxPowerValue { get; }

  /// <summary>Steam (m³/s) consumed per unit of delivered engine power.</summary>
  protected abstract float SteamPerPower { get; }

  /// <summary>Power this engine delivers for the given inlet steam pressure (0 = idle).</summary>
  protected abstract float ComputePower(float inletPressure);

  /// <summary>Top of the normal operating band (atm); sustained inlet pressure above this breaks the engine.</summary>
  protected abstract float OverPressureThreshold { get; }

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
    if (Api is { Side: EnumAppSide.Server })
      Api.World.PlaySoundAt(
        new AssetLocation("game:sounds/effect/anvilhit"),
        Pos.X + 0.5,
        Pos.Y + 0.5,
        Pos.Z + 0.5,
        null,
        false,
        16f
      );
    MarkDirty(true);
  }

  #endregion

  /// <summary>True once the player has finished the construction stages.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Available mechanical power (0..<see cref="MaxPower"/>), from inlet steam pressure.</summary>
  public float AvailablePower { get; private set; }

  /// <summary>Maximum power this engine can deliver to its sub-machine at rated pressure.</summary>
  public float MaxPower => MaxPowerValue;

  /// <summary>Cycle animation speed — the engine is the single source of truth for its sub-machine.</summary>
  public float AnimationSpeed { get; private set; } = 1f;

  /// <summary>True while the engine is running (sub-machine being driven).</summary>
  public bool IsRunning => _running;

  private BlockEngineBase? EngineBlock => Block as BlockEngineBase;

  private PipeNetwork? NetworkAt(BlockPos pos) =>
    _netSystem?.GetNetworkAt(pos) as PipeNetwork;

  /// <summary>The attached sub-machine block entity at the engine's sub-machine cell, if any.</summary>
  public IEngineSubmachine? SubmachineBE =>
    EngineBlock != null
      ? Api.World.BlockAccessor.GetBlockEntity(EngineBlock.SubmachinePos(Pos))
        as IEngineSubmachine
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

    // Available power from the inlet steam pressure (the curve is per-variant).
    var inlet = NetworkAt(EngineBlock.SteamInletPos(Pos));
    float pressure =
      inlet?.State?.GasType == "Steam" ? inlet.State.Pressure : 0f;

    // Over-pressure damage: running the engine above its band wears it out; sustained
    // long enough it bursts and must be repaired. Back inside the band, it recovers.
    if (pressure > OverPressureThreshold)
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

    float power = ComputePower(pressure);
    AvailablePower = power;

    // Demand-driven: only consume steam the sub-machine can actually use.
    float demand = SubmachineBE?.PowerDemand ?? 0f;
    bool run = power > 0f && demand > 0f;

    if (run && inlet != null)
    {
      float steam = SteamPerPower * power * demand * dt;
      float used = inlet.TryConsumeGas(steam, ba);
      // The spent steam leaves as hot condensed water on the east outlet.
      if (used > 0f)
      {
        var outNet = NetworkAt(EngineBlock.WaterOutletPos(Pos));
        outNet?.TryProduceLiquid(
          used * PpexValues.BoilerWaterPerSteam,
          90f,
          0f,
          ba
        );
      }
    }

    float newSpeed = run ? 0.5f + power : 1f;
    if (run != _running || Math.Abs(newSpeed - AnimationSpeed) > 0.05f)
    {
      AnimationSpeed = newSpeed;
      _running = run;
      MarkDirty(true); // sync running + speed to clients for the cycle animation
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

  public override void OnBlockRemoved()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    if (_submachineWatchId != 0)
      UnregisterGameTickListener(_submachineWatchId);
    if (_engineTickId != 0)
      UnregisterGameTickListener(_engineTickId);
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    if (_submachineWatchId != 0)
      UnregisterGameTickListener(_submachineWatchId);
    if (_engineTickId != 0)
      UnregisterGameTickListener(_engineTickId);
    base.OnBlockUnloaded();
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("running", _running);
    tree.SetFloat("animSpeed", AnimationSpeed);
    tree.SetFloat("availPower", AvailablePower);
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
    _running = tree.GetBool("running");
    AnimationSpeed = tree.GetFloat("animSpeed", 1f);
    AvailablePower = tree.GetFloat("availPower");
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
      // Re-pose on the client when the running state arrives so the cycle plays.
      else if (wasRunning != _running)
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
      dsc.AppendLine(
        Vintagestory.API.Config.Lang.Get(
          "ppex:engine-info-broken",
          EngineBlock?.RepairDescription ?? ""
        )
      );
      return;
    }

    dsc.AppendLine(
      Vintagestory.API.Config.Lang.Get(
        "ppex:engine-info-power",
        AvailablePower,
        MaxPower
      )
    );
    if (IsRunning)
      dsc.AppendLine(
        Vintagestory.API.Config.Lang.Get("ppex:engine-info-running")
      );
    if (_overPressureSeconds > 0f)
      dsc.AppendLine(
        Vintagestory.API.Config.Lang.Get(
          "ppex:engine-info-overpressure",
          OverPressureRemaining
        )
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

  // Includes a whole subtree when it doesn't contain the hidden element; otherwise
  // descends, dropping only the hidden branch.
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
      outList.Add(path);
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
  /// True when the sub-machine attached at <see cref="SubmachineLocal"/> is an
  /// MP generator (which uses the alternate <c>idlemp</c>/<c>cyclemp</c> animations).
  /// </summary>
  private bool IsMpGenerator()
  {
    // +180 to match the block's placement-angle offset (see BlockEngineBase).
    int angle =
      (StructureFillers.AngleFromSide(Block.Variant["side"]) + 180) % 360;
    Vec3i r = StructureFillers.RotateOffset(SubmachineLocal, angle);
    BlockPos cell = Pos.AddCopy(r.X, r.Y, r.Z);
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
    string code = _running
      ? (mp ? "cyclemp" : "cyclepump")
      : (mp ? "idlemp" : "idlepump");

    util.StartAnimation(
      new AnimationMetaData
      {
        Animation = code,
        Code = code,
        AnimationSpeed = _running ? AnimationSpeed : 1f,
        EaseInSpeed = 3f,
        EaseOutSpeed = 3f,
      }.Init()
    );
  }

  #endregion
}

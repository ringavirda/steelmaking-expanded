using System;
using ExpandedLib.Blocks.Construction;
using ExpandedLib.Blocks.Machines;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Shared base for the steam boilers - a mega-block raised via the vanilla
/// <c>RightClickConstructable</c> behavior, which suppresses the default mesh so the
/// vessel is drawn through the animator (a permanent <c>idle</c> animation re-tessellated
/// to the built elements as construction progresses, like the bessemer converter).
/// Peripheral cells are reserved with invisible structure fillers; verification,
/// completeness, projection and tick scheduling live in the multiblock base. Per-variant
/// stats are supplied through the virtual hooks below.
/// </summary>
public abstract class BlockEntityBoiler : BlockEntityMultiblockStructure {
  private BEBehaviorAnimatable? _animatable;
  private ExRightClickConstructable? _rcc;
  private bool _animatorReady;

  // Client-side in-vessel water surface + a tick to keep its state fresh.
  private BoilerWaterRenderer? _waterRenderer;
  private long _clientTickId;

  // Throttle stamp for the client-side boiling hum loop.
  private long _boilHumMs;

  #region Per-variant stats

  /// <summary>Total internal capacity (L) shared between water and steam.</summary>
  protected abstract float Capacity { get; }

  /// <summary>Minimum water (L) before the boiler will start heating/boiling.</summary>
  protected abstract float MinBoilWater { get; }

  /// <summary>Maximum water (L) the boiler will hold/boil - the rest of the capacity is steam space.</summary>
  protected abstract float MaxBoilWater { get; }

  /// <summary>
  /// Water (L) the automatic pump intake tops up to (a fraction of capacity), so a piped
  /// supply reaches a safe level without overfilling. Manual pouring can still reach
  /// <see cref="MaxBoilWater"/>.
  /// </summary>
  protected virtual float MaxWaterIntakeFill =>
    Capacity * PpexValues.BoilerWaterIntakeFillFraction;

  /// <summary>Steam (L/s) produced while boiling at full tilt.</summary>
  protected abstract float SteamPerSecond { get; }

  /// <summary>Steam pressure (atm) the boiler chokes its output network at.</summary>
  protected abstract float MaxOutputPressure { get; }

  protected abstract int ExplosionRadius { get; }

  #endregion

  /// <summary>True once the player has finished the construction stages.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>True only when the boiler may operate (built and structure complete).</summary>
  public bool IsOperational => IsConstructed && StructureComplete;

  /// <summary>Operating phase - the boiler runs like the blast furnace off a timer, not a temperature.</summary>
  public enum BoilerState {
    Idle,
    Heating,
    Boiling,
  }

  #region Operating state (serialized)

  /// <summary>Water held in the boiler (L).</summary>
  private float _waterVolume;

  /// <summary>Steam held internally (L); drives the internal pressure.</summary>
  private float _steamVolume;

  /// <summary>Current operating phase.</summary>
  private BoilerState _state = BoilerState.Idle;

  /// <summary>Seconds spent in the Heating phase (boils once it reaches the heat-up time).</summary>
  private float _heatingSeconds;

  /// <summary>Seconds the boiler has been running without fire / with water out of range (drives the shutdown grace).</summary>
  private float _shutdownSeconds;

  /// <summary>Whether the manual-access lid is open (held animation + venting + fill).</summary>
  public bool LidOpen { get; private set; }

  /// <summary>
  /// Transient (not serialized): set once a held right-click has toggled the lid, so the
  /// hold toggles exactly once instead of flipping every frame.
  /// </summary>
  public bool LidToggled { get; set; }

  #endregion

  /// <summary>
  /// Internal pressure (atm): steam over the tank space not occupied by water.
  /// e.g. 400 L steam, 400 L water, 1200 L vessel = 400 / (1200-400) = 0.5 atm.
  /// </summary>
  public float InternalPressure =>
    _steamVolume / Math.Max(1f, Capacity - _waterVolume);

  /// <summary>
  /// True while boiling at or above 90% of choke pressure - the burst-warning "danger
  /// zone". Derives from synced state, so it can drive the warning particles client-side.
  /// </summary>
  public bool InDangerZone =>
    _state == BoilerState.Boiling
    && InternalPressure >= 0.9f * MaxOutputPressure;

  /// <summary>Heating progress 0..1 (for the HUD); only meaningful in the Heating phase.</summary>
  public float HeatProgress =>
    GameMath.Clamp(_heatingSeconds / PpexValues.BoilerHeatUpSeconds, 0f, 1f);

  // In-game day stamp for natural water evaporation (no charge for unloaded time).
  private double _lastEvapDays = -1;

  // Client-display mirror, synced via the tree.
  private bool _burning;

  /// <summary>Set server-side when steam is escaping the outlet with no pipe attached;
  /// synced to drive the leak particle plume.</summary>
  private bool _steamLeaking;

  #region Lifecycle

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();
    _rcc = GetBehavior<ExRightClickConstructable>();

    if (api is ICoreClientAPI capi && _animatable != null) {
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;

      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      ApplyPose();

      InitWaterRenderer(capi);
      // Keep the water level / glow current despite push-based state syncing.
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
    }
    // The base (BlockEntityProductionMachine) registers the server production tick.
  }

  /// <summary>
  /// (Re)loads the multiblock definition for the current orientation, using the same angle
  /// the fillers use (see <see cref="BlockBoiler.StructureAngle"/>).
  /// </summary>
  protected override void UpdateStructureRotation() {
    if (BoilerBlock == null)
      return;
    SetStructureAngle(BoilerBlock.StructureAngle);
  }

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("ppex:structure-incomplete-count", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("ppex:structure-complete");

  private BlockBoiler? BoilerBlock => Block as BlockBoiler;

  /// <summary>Per-variant animator cache key (also the shape selector); unique per block code + side.</summary>
  protected virtual string AnimCacheKey => Block.Code.Path;

  public override void OnBlockRemoved() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    DisposeClient();
    // Base stops the monitor/production ticks and clears any structure projection.
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    DisposeClient();
    base.OnBlockUnloaded();
  }

  private void DisposeClient() {
    if (_clientTickId != 0) {
      UnregisterGameTickListener(_clientTickId);
      _clientTickId = 0;
    }
    _waterRenderer?.Dispose();
    _waterRenderer = null;
  }

  private void OnConstructShapeChanged(CompositeShape cs) {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyPose();
  }

  /// <summary>
  /// (Re)builds the animator to render exactly the currently-built elements. A fresh shape
  /// is loaded each call (reusing one re-maps UVs into atlas space and stretches textures).
  /// </summary>
  private void RebuildAnimator(string[]? selectiveElements) {
    if (Api is not ICoreClientAPI capi || _animatable == null)
      return;

    MeshData meshData = _animatable.animUtil.CreateMesh(
      AnimCacheKey,
      null,
      out Shape resolvedShape,
      null,
      new TesselationMetaData { SelectiveElements = selectiveElements }
    );

    var rotation = new Vec3f(0, Block.Shape.rotateY, 0);
    _animatable.animUtil.InitializeAnimator(
      AnimCacheKey,
      meshData,
      resolvedShape,
      rotation
    );
    // A failed shape resolve leaves animUtil.animator null; only mark ready when it truly
    // exists, so ApplyPose never queues an idle animation against a null animator (vanilla
    // GetBlockInfo would then NRE under extendedDebugInfo).
    _animatorReady = _animatable.animUtil.animator != null;

    // Swap vanilla's renderer for one that lights the vessel from a body cell rather than
    // the firebox-adjacent master cell (see BoilerAnimatableRenderer).
    if (_animatorReady && BoilerBlock != null) {
      var util = _animatable.animUtil;
      util.renderer?.Dispose();
      util.renderer = new BoilerAnimatableRenderer(
        capi,
        Pos.ToVec3d(),
        rotation,
        util.animator!,
        util.activeAnimationsByAnimCode,
        meshData
      ) {
        LightPos = BoilerBlock.LightSampleWorldPos(Pos).ToVec3d(),
        // Seed visibility from whether a pose is already running. 1.22's AnimatableRenderer
        // ctor does this itself; the legacy (1.20/1.21) ctor leaves ShouldRender false and only
        // flips it via OnAnimationsStateChange, which StartAnimation skips when the pose ("idle")
        // is already active. Without this, a rebuild on each construction step (OnConstructShape
        // Changed) births an invisible renderer and the vessel mesh vanishes after the first step.
        ShouldRender = util.activeAnimationsByAnimCode.Count > 0,
      };
    }
  }

  private void ApplyPose() {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;

    // Animatable only draws while an animation runs. "idle" holds the built mesh at rest;
    // "lidopen" holds it with the lid open. Both drive the lid, so swap based on lid state.
    if (LidOpen) {
      util.StopAnimation("idle");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = "lidopen",
          Code = "lidopen",
          AnimationSpeed = 1f,
          EaseInSpeed = 6f,
          EaseOutSpeed = 6f,
        }.Init()
      );
    } else {
      util.StopAnimation("lidopen");
      util.StartAnimation(
        new AnimationMetaData {
          Animation = "idle",
          Code = "idle",
          AnimationSpeed = 1f,
          EaseInSpeed = 6f,
          EaseOutSpeed = 6f,
        }.Init()
      );
    }
  }

  #endregion

  #region Production

  /// <summary>Grace timer: how long the boiler has sat at its output ceiling while still boiling (drives the explosion).</summary>
  private GraceTimer _overpressure;

  /// <summary>Grace timer: how long the boiler has sat choked - fire lit but its exhaust outlet backed up (drives snuffing the fuel pile).</summary>
  private GraceTimer _chokeTimer;

  /// <summary>Whether the boiler is currently choked (can't expel exhaust). Synced for the HUD line.</summary>
  private bool _choked;

  protected override void OnProductionTick(float dt) {
    if (!IsConstructed)
      return;

    var ba = Api.World.BlockAccessor;

    ApplyEvaporation();

    BlockPos fuelPos = BoilerBlock?.FuelWorldPos(Pos) ?? Pos;
    var pile = ba.GetBlockEntity(fuelPos) as BlockEntityCoalPile;
    bool fireOn =
      pile?.IsBurning == true
      && pile.inventory is { Count: > 0 }
      && !pile.inventory[0].Empty;

    PipeNetwork? exhaustNet =
      BoilerBlock != null
        ? NetworkAt<PipeNetwork>(BoilerBlock.ExhaustOutletWorldPos(Pos))
        : null;
    bool draughtBlocked =
      (exhaustNet?.State?.Pressure ?? 0f)
      >= PpexValues.ExhaustMaxOutputPressure;
    bool burning = fireOn && !draughtBlocked;

    // Fire lit but exhaust outlet backed up to the vent cap = choked: combustion gas can't
    // escape. Sit choked too long and the fuel pile is snuffed (like a blocked flue).
    _choked = fireOn && draughtBlocked;
    if (
      _chokeTimer.Update(_choked, dt, PpexValues.BoilerChokeExtinguishSeconds)
    ) {
      pile?.Extinguish();
      ExSounds.Play(Api, fuelPos, ExSounds.Extinguish, 0.7f);
      _choked = false;
    }

    PipeNetwork? waterNet = ConnectedNetwork<PipeNetwork>(BlockFacing.DOWN);
    if (waterNet != null && _waterVolume < MaxWaterIntakeFill) {
      float feedPressure = waterNet.State?.Pressure ?? 0f;
      // Cap the draw at the intake rate so a piped supply trickles in (≤10 L/s) instead of
      // gulping the whole remaining headroom in a single tick.
      float request = Math.Min(
        MaxWaterIntakeFill - _waterVolume,
        PpexValues.BoilerWaterIntakeRate * dt
      );
      float drawn = waterNet.TryConsumeLiquid(request, ba);
      _waterVolume += drawn;

      if (drawn > 0f && feedPressure > 1f && _state == BoilerState.Boiling)
        _steamVolume +=
          drawn * (feedPressure - 1f) * PpexValues.WaterPressureSteamBoost;
    }

    // Only a lower water bound gates boiling - there's no "too full" cutoff, since the fill
    // paths (auto intake, manual pour, condensation) already cap water at MaxBoilWater.
    bool enoughWater = _waterVolume >= MinBoilWater;
    float grace = PpexValues.BoilerShutdownDelaySeconds;

    switch (_state) {
      case BoilerState.Idle:
        CondenseInternal(dt);
        if (burning && enoughWater) {
          _state = BoilerState.Heating;
          _heatingSeconds = 0f;
          _shutdownSeconds = 0f;
        }
        break;

      case BoilerState.Heating:
        if (!burning || !enoughWater) {
          _shutdownSeconds += dt;
          if (_shutdownSeconds >= grace)
            ShutDown();
        } else {
          _shutdownSeconds = 0f;
          _heatingSeconds += dt;
          if (_heatingSeconds >= PpexValues.BoilerHeatUpSeconds)
            _state = BoilerState.Boiling;
        }
        break;

      case BoilerState.Boiling:
        if (burning && enoughWater)
          _shutdownSeconds = 0f;
        else {
          _shutdownSeconds += dt;
          if (_shutdownSeconds >= grace) {
            ShutDown();
            break;
          }
        }

        if (
          enoughWater
          && (burning || _shutdownSeconds < grace)
          && InternalPressure < MaxOutputPressure
        )
          BoilStep(dt);
        break;
    }

    _burning = burning && _state != BoilerState.Idle;

    if (LidOpen) {
      VentExcessSteam(dt);
      _overpressure.Reset();
      _steamLeaking = false; // steam vents through the lid, not the outlet
    } else {
      // PushSteam reports back when the outlet is open to air (no pipe) and steam is
      // jetting out instead of pressurising - that drives the leak particles.
      _steamLeaking = _state != BoilerState.Idle && PushSteam(ba, dt);

      bool overPressure =
        _state == BoilerState.Boiling
        && burning
        && InternalPressure >= MaxOutputPressure;
      if (
        _overpressure.Update(
          overPressure,
          dt,
          PpexValues.BoilerOverpressureSeconds
        )
      ) {
        Explode();
        return;
      }
    }

    if (burning && exhaustNet != null)
      exhaustNet.TryProduceGas(
        PpexValues.BoilerExhaustPerSecond * dt,
        SteamTemperature() * 0.6f,
        "Exhaust",
        ba,
        maxOutputPressure: PpexValues.ExhaustMaxOutputPressure
      );

    MarkDirty(true);
  }

  /// <summary>Converts water to steam for one tick (1 L water → <see cref="PpexValues.SteamExpansionFactor"/> L steam).</summary>
  private void BoilStep(float dt) {
    float waterUse = Math.Min(
      _waterVolume,
      SteamPerSecond * dt / PpexValues.SteamExpansionFactor
    );
    if (waterUse <= 0f)
      return;
    _waterVolume -= waterUse;
    _steamVolume += waterUse * PpexValues.SteamExpansionFactor;
  }

  /// <summary>
  /// Saturated-steam temperature (°C), derived from the internal pressure the way real steam
  /// works: T = boiling point × absolutePressure^exponent. Higher pressure means hotter steam.
  /// The boiler/pipe pressure is gauge (0 atm = atmospheric), so add the 1 atm atmosphere to get
  /// the absolute pressure - at 0 atm gauge the steam reads exactly the boiling point.
  /// </summary>
  private float SteamTemperature() {
    float absPressure = Math.Max(0f, InternalPressure) + 1f;
    return PpexValues.BoilingPoint
      * (float)Math.Pow(absPressure, PpexValues.SteamSaturationExponent);
  }

  /// <summary>
  /// Pushes internal steam into the steam network, capped at the choke pressure. With no
  /// connected steam pipe at the outlet, the neck is open: steam bleeds to atmosphere at
  /// <see cref="PpexValues.BoilerSteamLeakRate"/> and the method returns <c>true</c> to
  /// drive the leak particles.
  /// </summary>
  private bool PushSteam(IBlockAccessor ba, float dt) {
    // The steam connector is the port filler atop the body; the network it feeds sits in
    // the cell directly above it.
    var connectorPos = BoilerBlock?.SteamPipeWorldPos(Pos);
    if (connectorPos == null || _steamVolume <= 0f)
      return false;
    BlockPos pipePos = connectorPos.UpCopy();

    bool pipeAttached =
      ba.GetBlock(pipePos) is BlockNetworkNode steamPipe
      && steamPipe.HasConnectorAt(BlockFacing.DOWN);

    if (!pipeAttached) {
      // Open neck - steam jets out instead of building pressure.
      float leaked = Math.Min(
        _steamVolume,
        PpexValues.BoilerSteamLeakRate * dt
      );
      _steamVolume = Math.Max(0f, _steamVolume - leaked);
      return leaked > 0f;
    }

    PipeNetwork? steamNet = NetworkAt<PipeNetwork>(pipePos);
    if (steamNet == null)
      return false;

    // A freshly built run has no PipeNetworkState (created lazily on first TryProduceGas).
    // Treat that as an empty network at full node capacity so the boiler can charge it.
    var st = steamNet.State;
    float netVolume = st?.Volume ?? 0f;
    float netMaxVolume =
      st?.MaxVolume ?? steamNet.Nodes.Count * PpexValues.LitresPerPipe;
    if (netMaxVolume <= 0f)
      return false;

    // Boiler and pipe run are connected vessels: move steam until pressures equalise, so it
    // always stays in both (the boiler never empties into the run). Transfer = boiler steam
    // above the shared equilibrium pressure (free vessel space F, pipe capacity V):
    //   eqP = (Sboiler + Spipe) / (F + V);  transfer = Sboiler − eqP·F.
    float freeSpace = Math.Max(1f, Capacity - _waterVolume);
    float eqPressure = (_steamVolume + netVolume) / (freeSpace + netMaxVolume);
    float transfer = _steamVolume - eqPressure * freeSpace;
    if (transfer <= 0.001f)
      return false; // pipe already at/above the boiler's pressure - hold the steam in

    float accepted = steamNet.ProduceGasMeasured(
      transfer,
      SteamTemperature(),
      "Steam",
      ba,
      maxOutputPressure: InternalPressure
    );
    if (accepted > 0f)
      _steamVolume = Math.Max(0f, _steamVolume - accepted);
    return false;
  }

  /// <summary>
  /// Bleeds steam out through the open lid at <see cref="PpexValues.BoilerLidVentRate"/>, so a
  /// pressurised vessel blows off gradually. While running it stops at atmospheric (1 atm) since
  /// boiling keeps adding steam underneath; once idle it empties the trapped pocket completely
  /// (down to 0 atm), so an open lid fully drains a shut-down boiler.
  /// </summary>
  private void VentExcessSteam(float dt) {
    float floor =
      _state == BoilerState.Idle ? 0f : Math.Max(0f, Capacity - _waterVolume);
    if (_steamVolume <= floor)
      return;
    float vent = Math.Min(
      _steamVolume - floor,
      PpexValues.BoilerLidVentRate * dt
    );
    _steamVolume = Math.Max(floor, _steamVolume - vent);
  }

  /// <summary>
  /// Condenses leftover internal steam back into water (after a shutdown), but only while the
  /// resulting water stays below the boil-water ceiling (<see cref="MaxBoilWater"/>). Once the
  /// vessel is that full, the remaining steam is trapped and stops condensing - the player vents
  /// it through the lid. The leftover headspace above the water keeps the steam pocket finite.
  /// </summary>
  private void CondenseInternal(float dt) {
    if (_steamVolume <= 0f)
      return;
    // Condensing steam removes 16 L of steam but only frees ~1 L of headspace (the water it
    // becomes), so in a nearly-full, high-pressure vessel each step concentrates the leftover
    // steam and RAISES pressure instead of lowering it - the crossover is exactly at the
    // expansion factor. Above it, refuse to condense: the trapped steam must be bled off
    // through the lid first, so condensation can never run the pressure up toward a burst.
    if (InternalPressure >= PpexValues.SteamExpansionFactor)
      return;
    float waterRoom = MaxBoilWater - _waterVolume;
    if (waterRoom <= 0f)
      return;
    float cond = Math.Min(
      Math.Min(_steamVolume, PpexValues.BoilerShutdownCondenseRate * dt),
      waterRoom * PpexValues.SteamExpansionFactor
    );
    _steamVolume -= cond;
    _waterVolume += cond / PpexValues.SteamExpansionFactor;
  }

  /// <summary>Shuts the boiler down: back to Idle, reset timers (leftover steam condenses in Idle).</summary>
  private void ShutDown() {
    _state = BoilerState.Idle;
    _heatingSeconds = 0f;
    _shutdownSeconds = 0f;
    _burning = false;
  }

  /// <summary>Natural water evaporation (in-game time based; nothing charged for time the chunk was unloaded).</summary>
  private void ApplyEvaporation() {
    double nowDays = Api.World.Calendar?.TotalDays ?? -1;
    if (nowDays < 0)
      return;
    if (_lastEvapDays >= 0 && _waterVolume > 0f) {
      float evap = (float)(
        PpexValues.EvaporationLitresPerDay * (nowDays - _lastEvapDays)
      );
      if (evap > 0f)
        _waterVolume = Math.Max(0f, _waterVolume - evap);
    }
    _lastEvapDays = nowDays;
  }

  private void Explode() {
    BlockPos pos = Pos.Copy();
    var world = Api.World;
    // Centre the blast on the vessel body, not the master cell (at the firebox end).
    BlockPos center = BoilerBlock?.ExplosionCenterPos(pos) ?? pos;

    if (BoilerBlock != null) {
      // A burst skips the structure's normal break path, so pull a salvageable fraction of
      // the build materials straight from the RightClickConstructable behavior.
      foreach (
        var ds in ConstructionMaterialDrops(PpexValues.BoilerExplosionDropRatio)
      )
        world.SpawnItemEntity(ds, pos.ToVec3d().Add(0.5, 0.5, 0.5));
      BoilerBlock.RemoveStructure(world, pos);
    }
    world.BlockAccessor.SetBlock(0, pos);

    // The built-in explosion supplies particles, sound, drops and entity damage but spares
    // the mod's low-resistance machinery, so flatten the fragile blocks (pipes, ports, coal
    // piles, soft terrain) ourselves first. Per-variant radius. Runs in the server tick, so
    // the server-world cast is valid.
    float r = ExplosionRadius;
    ShatterFragileBlocks(
      world,
      center,
      r,
      PpexValues.BoilerBlastResistanceThreshold
    );
    (world as IServerWorldAccessor)?.CreateExplosion(
      center,
      EnumBlastType.EntityBlast,
      r,
      r + 2f
    );
  }

  /// <summary>
  /// The build materials this boiler would drop at <paramref name="ratio"/> (0..1) of the
  /// consumed stacks. The construction behavior only scatters these from its own
  /// <c>OnBlockBroken</c> at a fixed ratio, so go through its dedicated accessor. Returns empty
  /// if the behavior is missing (never throws).
  /// </summary>
  private ItemStack[] ConstructionMaterialDrops(float ratio) =>
    _rcc?.GetConstructionDrops(ratio, Api.World.Rand) ?? [];

  /// <summary>
  /// Breaks every block within <paramref name="radius"/> of <paramref name="center"/> below
  /// <paramref name="maxResistance"/> - pipes, ports, coal piles, soft terrain - leaving
  /// sturdier blocks standing. Uses <c>BreakBlock</c> so pipe nodes detach cleanly.
  /// </summary>
  private static void ShatterFragileBlocks(
    IWorldAccessor world,
    BlockPos center,
    float radius,
    float maxResistance
  ) {
    var ba = world.BlockAccessor;
    int ri = (int)Math.Ceiling(radius);
    float r2 = radius * radius;
    for (int dx = -ri; dx <= ri; dx++)
      for (int dy = -ri; dy <= ri; dy++)
        for (int dz = -ri; dz <= ri; dz++) {
          if (dx * dx + dy * dy + dz * dz > r2)
            continue;
          BlockPos p = center.AddCopy(dx, dy, dz);
          Block block = ba.GetBlock(p);
          if (block.Id == 0 || block.Resistance >= maxResistance)
            continue;
          ba.BreakBlock(p, null, 0.25f);
        }
  }

  #endregion

  #region Lid + manual fill

  /// <summary>Toggles the manual-access lid (sprint + RMB on the boiler).</summary>
  public void ToggleLid() {
    LidOpen = !LidOpen;

    // Borrow the coke-oven door's metal hatch open/close sound for the lid.
    var sound = LidOpen
      ? ExSounds.CokeOvenDoorOpen
      : ExSounds.CokeOvenDoorClose;
    BlockPos lidPos = BoilerBlock?.LidWorldPos(Pos) ?? Pos;
    ExSounds.PlayAt(Api.World, lidPos, sound, null, range: 32f);

    MarkDirty(true);
  }

  /// <summary>
  /// Pours water from a held liquid container into the boiler (RMB while the lid is open),
  /// capped by the boil-water ceiling. Both are metered in litres, so no conversion. The
  /// kickstart before the pump.
  /// </summary>
  public bool TryManualFill(IPlayer byPlayer, ItemSlot slot) {
    if (slot.Itemstack?.Collectible is not BlockLiquidContainerBase cont)
      return false;

    ItemStack? content = cont.GetContent(slot.Itemstack);
    if (content?.Collectible?.Code?.Path?.Contains("water") != true)
      return false;

    float space = MaxBoilWater - _waterVolume;
    if (space < 0.01f)
      return false;

    // Empty the container, capped by space left; measure via the litre delta so
    // transfer-size rounding can't desync the amounts.
    float before = cont.GetCurrentLitres(slot.Itemstack);
    if (before <= 0f)
      return false;

    cont.TryTakeLiquid(slot.Itemstack, Math.Min(before, space));
    float removed = before - cont.GetCurrentLitres(slot.Itemstack);
    if (removed <= 0f)
      return false;
    slot.MarkDirty();

    _waterVolume += removed;

    BlockPos pourPos = BoilerBlock?.LidWorldPos(Pos) ?? Pos;
    ExSounds.PlayAt(Api.World, pourPos, ExSounds.WaterPour, null, range: 16f);

    MarkDirty(true);
    return true;
  }

  /// <summary>
  /// Bails water out of the boiler into a held liquid container (RMB with an empty/water-holding
  /// bucket while the lid is open). Only water above <see cref="MinBoilWater"/> is reachable - the
  /// boiler's operating floor is too deep for a bucket - so manual draining stops there. Metered
  /// in litres on both sides via the litre delta, so transfer-size rounding can't desync them.
  /// </summary>
  public bool TryManualDrain(IPlayer byPlayer, ItemSlot slot) {
    if (slot.Itemstack?.Collectible is not BlockLiquidContainerBase cont)
      return false;

    // Empty, or already holding water (don't mix into milk/other liquids).
    ItemStack? content = cont.GetContent(slot.Itemstack);
    if (
      content != null
      && content.Collectible?.Code?.Path?.Contains("water") != true
    )
      return false;

    // Only water above the operating floor can be bailed out with a bucket.
    float reachable = _waterVolume - MinBoilWater;
    if (reachable < 0.01f)
      return false;

    float before = cont.GetCurrentLitres(slot.Itemstack);
    float space = cont.CapacityLitres - before;
    if (space < 0.01f)
      return false;

    float want = Math.Min(reachable, space);
    var waterStack = new ItemStack(
      Api.World.GetItem(new AssetLocation("game:waterportion"))
    );
    // TryPutLiquid also caps the transfer at the liquid stack's item count, and a fresh portion
    // stack is one item (0.01 L). Make the source effectively unlimited so the bucket fills right
    // up to its free space (a full 10 L bucket each time) - the transfer is then bounded only by
    // `want` (the bucket's free space, capped by the reachable boiler water).
    waterStack.StackSize = int.MaxValue;
    cont.TryPutLiquid(slot.Itemstack, waterStack, want);
    float added = cont.GetCurrentLitres(slot.Itemstack) - before;
    if (added <= 0f)
      return false;
    slot.MarkDirty();

    _waterVolume -= added;

    BlockPos drainPos = BoilerBlock?.LidWorldPos(Pos) ?? Pos;
    ExSounds.PlayAt(Api.World, drainPos, ExSounds.WaterPour, null, range: 16f);

    MarkDirty(true);
    return true;
  }

  #endregion

  #region Client rendering + particles

  // Water/steam boxes are authored in the structure-offset frame (body along local +z), so
  // they rotate by the SAME angle the fillers/connectors use (StructureAngle = AngleFromSide
  // + 180), NOT Shape.rotateY - the two differ by 180°, which would swing the water surface
  // onto the firebox/hatch side.
  private float StructureRotationRad =>
    (float)((BoilerBlock?.StructureAngle ?? 0) * Math.PI / 180.0);

  /// <summary>
  /// In-vessel water-surface footprint (0-16 pixel space, block-local), read from the
  /// block's <c>waterRendererBox</c> attribute. Falls back to a 3-deep box.
  /// </summary>
  protected virtual Cuboidf[] WaterRendererBoxes {
    get {
      var node = (Block as IBoilerGeometry)?.WaterRendererBox;
      if (node == null || !node.Exists)
        return [new Cuboidf(-16f, 0f, 0f, 16f, 16f, 48f)];

      return
      [
        new Cuboidf(
          node["x1"].AsFloat(-16f),
          node["y1"].AsFloat(0f),
          node["z1"].AsFloat(0f),
          node["x2"].AsFloat(16f),
          node["y2"].AsFloat(16f),
          node["z2"].AsFloat(48f)
        ),
      ];
    }
  }

  private void InitWaterRenderer(ICoreClientAPI capi) {
    // The box supplies the horizontal footprint + UV; surface height is driven in discrete
    // steps via SurfaceLevel (see OnClientTick).
    _waterRenderer = new BoilerWaterRenderer(
      Pos,
      capi,
      WaterRendererBoxes,
      StructureRotationRad
    );
    capi.Event.RegisterRenderer(_waterRenderer, EnumRenderStage.Opaque);
  }

  private void OnClientTick(float dt) {
    if (_waterRenderer != null) {
      // Discrete surface height: hidden when dry, low (below the flues) while filling
      // toward the operating threshold, high (above the flues) once it can operate.
      _waterRenderer.SurfaceLevel =
        _waterVolume <= 0.01f ? 0f
        : _waterVolume < MinBoilWater ? PpexValues.BoilerWaterSurfaceLowLevel
        : PpexValues.BoilerWaterSurfaceHighLevel;
      _waterRenderer.Temperature = DisplayWaterTemperature();
    }

    // A boiling boiler rumbles (lava bubble/rumble loop, tuned low) from the vessel body.
    if (_state == BoilerState.Boiling)
      ExSounds.PlayLoop(
        Api.World,
        BoilerBlock?.LidWorldPos(Pos) ?? Pos,
        ExSounds.Lava,
        ref _boilHumMs,
        2500,
        0.4f,
        16f
      );

    // Near choke pressure, vent warning steam so the player sees it's about to burst.
    if (InDangerZone)
      SpawnDangerSteam();

    // Steam escapes only through the open lid; a sealed boiler keeps it contained.
    if (LidOpen && _steamVolume > 0f)
      SpawnLidSteam();

    // ...unless the outlet neck has no pipe attached, where it jets out instead.
    if (_steamLeaking)
      SpawnOutletLeakSteam();
  }

  /// <summary>Warning steam erupting from the access-lid filler while in the danger zone.</summary>
  private void SpawnDangerSteam() {
    if (Api is not ICoreClientAPI || BoilerBlock == null)
      return;
    EmitSteamPlume(BoilerBlock.LidWorldPos(Pos), 4);
  }

  /// <summary>Water surface temperature for the renderer glow, derived from the operating phase.</summary>
  private float DisplayWaterTemperature() =>
    _state switch {
      BoilerState.Boiling => PpexValues.BoilingPoint,
      BoilerState.Heating => 20f
        + (PpexValues.BoilingPoint - 20f) * HeatProgress,
      _ => 20f,
    };

  /// <summary>Steam billowing out of the open access lid (see <see cref="BlockBoiler.LidWorldPos"/>).</summary>
  private void SpawnLidSteam() {
    if (BoilerBlock != null)
      EmitSteamPlume(BoilerBlock.LidWorldPos(Pos).AddCopy(0, -1, 0), 6);
  }

  /// <summary>Steam jetting out of the outlet neck when no pipe is attached above it
  /// (see <see cref="BlockBoiler.SteamPipeWorldPos"/>).</summary>
  private void SpawnOutletLeakSteam() {
    if (BoilerBlock != null)
      EmitSteamPlume(BoilerBlock.SteamPipeWorldPos(Pos), 8);
  }

  /// <summary>Spawns a short-lived steam plume rising out of the top of <paramref name="cell"/>.</summary>
  private void EmitSteamPlume(BlockPos cell, int count) {
    if (Api is ICoreClientAPI) {
      ExParticles.SteamPlume(Api.World, cell, count);
      ExSounds.HissSound(Api.World, cell);
    }
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetFloat("waterVolume", _waterVolume);
    tree.SetFloat("steamVolume", _steamVolume);
    tree.SetInt("boilerState", (int)_state);
    tree.SetFloat("heatingSeconds", _heatingSeconds);
    tree.SetFloat("shutdownSeconds", _shutdownSeconds);
    tree.SetBool("lidOpen", LidOpen);
    tree.SetBool("burning", _burning);
    tree.SetBool("steamLeaking", _steamLeaking);
    _overpressure.ToTree(tree, "overpressure");
    tree.SetBool("choked", _choked);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _waterVolume = tree.GetFloat("waterVolume");
    _steamVolume = tree.GetFloat("steamVolume");
    _state = (BoilerState)tree.GetInt("boilerState");
    _heatingSeconds = tree.GetFloat("heatingSeconds");
    _shutdownSeconds = tree.GetFloat("shutdownSeconds");
    bool prevLidOpen = LidOpen;
    LidOpen = tree.GetBool("lidOpen");
    _burning = tree.GetBool("burning");
    _steamLeaking = tree.GetBool("steamLeaking");

    // Lid pose is push-based: replay it whenever the synced state flips.
    if (Api?.Side == EnumAppSide.Client && prevLidOpen != LidOpen)
      ApplyPose();
    _overpressure.FromTree(tree, "overpressure");
    _choked = tree.GetBool("choked");
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  ) {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed)
      return;

    if (!StructureComplete) {
      UpdateStructureRotation();
      int missing = _structure?.InCompleteBlockCount(Api.World, Pos) ?? 0;
      dsc.AppendLine(Lang.Get("ppex:structure-incomplete-count", missing));
      return;
    }

    dsc.AppendLine(
      Lang.Get(
        "ppex:boiler-info-water",
        ExMeasure.VolumeRange(_waterVolume, MaxBoilWater)
      )
    );
    dsc.AppendLine(
      Lang.Get(
        "ppex:boiler-info-steam",
        ExMeasure.Volume(_steamVolume),
        ExMeasure.Pressure(InternalPressure)
      )
    );

    if (_state == BoilerState.Boiling)
      dsc.AppendLine(
        Lang.Get(
          "ppex:boiler-info-boiling",
          ExMeasure.FlowRate(SteamPerSecond, "F0"),
          ExMeasure.Temperature(SteamTemperature())
        )
      );
    else if (_state == BoilerState.Heating)
      dsc.AppendLine(Lang.Get("ppex:boiler-info-heating", HeatProgress * 100f));
    else if (_waterVolume < MinBoilWater)
      dsc.AppendLine(
        Lang.Get("ppex:boiler-info-needswater", ExMeasure.Volume(MinBoilWater))
      );
    else
      dsc.AppendLine(Lang.Get("ppex:boiler-info-idle"));

    if (LidOpen)
      dsc.AppendLine(Lang.Get("ppex:boiler-info-lidopen"));

    if (_choked)
      dsc.AppendLine(Lang.Get("ppex:boiler-info-choked"));

    if (_overpressure.IsCounting)
      dsc.AppendLine(
        Lang.Get(
          "ppex:boiler-info-overpressure",
          _overpressure.Remaining(PpexValues.BoilerOverpressureSeconds)
        )
      );
  }

  #endregion
}

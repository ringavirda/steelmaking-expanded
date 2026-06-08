using System;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Boiler.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;

/// <summary>
/// Shared base for the steam boilers — a single mega-block that renders across its
/// footprint and is raised in place via the vanilla <c>RightClickConstructable</c>
/// behavior.
/// <para>
/// RightClickConstructable suppresses the default block mesh, so (like the bessemer
/// converter) the vessel is drawn through the animator: a permanently-running
/// <c>idle</c> animation keeps it visible, re-tessellated to the currently-built
/// elements whenever the construction stage changes.
/// </para>
/// <para>
/// The surrounding cells are reserved with invisible structure fillers
/// (see <see cref="StructureFillers"/>); the steam outlet attaches at the cell
/// named by the block's <c>outletOffset</c> attribute. Per-variant stats (capacity,
/// conversion rate, pressures) are supplied through the virtual hooks below.
/// </para>
/// </summary>
public abstract class BlockEntityBoilerBase : BlockEntityMultiblockStructure
{
  private BEBehaviorAnimatable? _animatable;
  private BEBehaviorRightClickConstructable? _rcc;
  private bool _animatorReady;

  // The boiler is both an RCC mega-block and a multiblock structure: its peripheral
  // cells (coke-oven door, firebox, steam & exhaust outlets) must be in place before
  // it can operate. The structure verification, completeness state, projection and
  // tick scheduling all live in BlockEntityMultiblockStructure.

  // Client-side in-vessel water surface + a tick to keep its state fresh.
  private BoilerWaterRenderer? _waterRenderer;
  private long _clientTickId;

  #region Per-variant stats

  /// <summary>Maximum water (m³) the boiler can hold.</summary>
  protected abstract float WaterCapacity { get; }

  /// <summary>Maximum internal steam (m³) the boiler can hold before venting/choking caps it.</summary>
  protected abstract float MaxInternalSteam { get; }

  /// <summary>Steam (m³/s) produced per second while boiling at full tilt.</summary>
  protected abstract float SteamPerSecond { get; }

  /// <summary>Steam pressure (atm) the boiler chokes at on the outlet network.</summary>
  protected abstract float MaxOutputPressure { get; }

  /// <summary>Cap (°C) on the boiler's internal (firebox) temperature.</summary>
  protected abstract float MaxTemperature { get; }

  #endregion

  /// <summary>True once the player has finished the construction stages.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>True only when the boiler may operate (built and structure complete).</summary>
  public bool IsOperational => IsConstructed && StructureComplete;

  /// <summary>World position of the linked steam outlet, if any.</summary>
  public BlockPos? OutletPos { get; private set; }

  #region Operating state (serialized)

  /// <summary>Firebox/vessel temperature (°C).</summary>
  private float _internalTemperature = 20f;

  /// <summary>Temperature (°C) of the water held in the boiler.</summary>
  private float _waterTemperature = 20f;

  /// <summary>Water held in the boiler (m³).</summary>
  private float _waterVolume;

  /// <summary>Steam held internally (m³); drives the internal pressure.</summary>
  private float _internalSteam;

  /// <summary>Whether the manual-access lid is open (held animation + venting + fill).</summary>
  public bool LidOpen { get; private set; }

  #endregion

  /// <summary>Internal pressure (atm) = stored steam over its max.</summary>
  public float InternalPressure =>
    MaxInternalSteam > 0f ? _internalSteam / MaxInternalSteam : 0f;

  // Client-display mirror, synced via the tree.
  private bool _burning;

  #region Lifecycle

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();
    _rcc = GetBehavior<BEBehaviorRightClickConstructable>();

    if (api is ICoreClientAPI capi && _animatable != null)
    {
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;

      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      ApplyPose();

      InitWaterRenderer(capi);
      // Keep the water level / glow current despite push-based state syncing.
      _clientTickId = RegisterGameTickListener(OnClientTick, 250);
    }

    if (api.Side == EnumAppSide.Server)
      _netSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
  }

  /// <summary>
  /// (Re)loads the multiblock definition for the current orientation. The structure is
  /// laid out with the same angle the fillers use (see <see cref="BlockBoilerBase.StructureAngle"/>).
  /// Completeness, the projection and tick scheduling are handled by the base class.
  /// </summary>
  protected override void UpdateStructureRotation()
  {
    if (BoilerBlock == null)
      return;

    int angle = BoilerBlock.StructureAngle;
    if (_structure == null || _currentAngle != angle)
    {
      _structure = Block.Attributes?[
        "multiblockStructure"
      ]?.AsObject<MultiblockStructure>();
      _structure?.InitForUse(angle);
      _currentAngle = angle;
    }
  }

  protected override string GetIncompleteMessage(int missingCount) =>
    Lang.Get("ppex:structure-incomplete-count", missingCount);

  protected override string GetCompleteMessage() =>
    Lang.Get("ppex:structure-complete");

  private BlockNetworkModSystem? _netSystem;
  private BlockBoilerBase? BoilerBlock => Block as BlockBoilerBase;

  private PipeNetwork? NetworkAt(BlockPos pos) =>
    _netSystem?.GetNetworkAt(pos) as PipeNetwork;

  /// <summary>Per-variant animator cache key (also the shape selector); unique per block code + side.</summary>
  protected virtual string AnimCacheKey => Block.Code.Path;

  public override void OnBlockRemoved()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    DisposeClient();
    // Base stops the monitor/production ticks and clears any structure projection.
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    DisposeClient();
    base.OnBlockUnloaded();
  }

  private void DisposeClient()
  {
    if (_clientTickId != 0)
    {
      UnregisterGameTickListener(_clientTickId);
      _clientTickId = 0;
    }
    _waterRenderer?.Dispose();
    _waterRenderer = null;
  }

  private void OnConstructShapeChanged(CompositeShape cs)
  {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyPose();
  }

  /// <summary>
  /// (Re)builds the animator so it renders exactly the currently-built elements.
  /// A fresh shape is loaded each call (reusing one would re-map UVs into atlas
  /// space repeatedly and stretch textures); rotation is applied by the renderer.
  /// </summary>
  private void RebuildAnimator(string[]? selectiveElements)
  {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    MeshData meshData = _animatable.animUtil.CreateMesh(
      AnimCacheKey,
      null,
      out Shape resolvedShape,
      null,
      new TesselationMetaData { SelectiveElements = selectiveElements }
    );

    _animatable.animUtil.InitializeAnimator(
      AnimCacheKey,
      meshData,
      resolvedShape,
      new Vec3f(0, Block.Shape.rotateY, 0)
    );
    // CreateMesh returns a null shape when the block's shape asset fails to
    // resolve, in which case InitializeAnimator leaves animUtil.animator null.
    // Only mark ready when the animator truly exists, so ApplyPose never queues
    // an idle animation against a null animator (vanilla GetBlockInfo would then
    // NRE iterating activeAnimationsByAnimCode under extendedDebugInfo).
    _animatorReady = _animatable.animUtil.animator != null;
  }

  private void ApplyPose()
  {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    util.StopAnimation("idle");

    // The boiler has no operating poses yet; "idle" simply holds the built mesh
    // visible (Animatable only draws while an animation is running).
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

  #endregion

  #region Outlet link

  /// <summary>Records (or clears) the steam outlet bound to this boiler.</summary>
  public void LinkOutlet(BlockPos? outletPos)
  {
    OutletPos = outletPos?.Copy();
    MarkDirty(true);
  }

  #endregion

  #region Production

  private float _fuelAccum;

  /// <summary>Seconds the boiler has sat fully choked while still boiling (drives the explosion).</summary>
  private float _overpressureSeconds;

  protected override void OnProductionTick(float dt)
  {
    // The base class only schedules this tick while the structure is complete; also
    // require the vessel itself to have been raised (RCC construction finished).
    if (!IsConstructed)
      return;

    var ba = Api.World.BlockAccessor;

    // --- Lid open: rapid emergency vent toward 1 atm / 60 °C, no production. ---
    if (LidOpen)
    {
      float k = Math.Min(1f, PpexValues.BoilerLidVentSpeed * dt);
      _internalTemperature +=
        (PpexValues.BoilerLidCoolTarget - _internalTemperature) * k;
      _waterTemperature +=
        (PpexValues.BoilerLidCoolTarget - _waterTemperature) * k;
      float ventSteam = PpexValues.BoilerLidVentPressure * MaxInternalSteam;
      if (_internalSteam > ventSteam)
        _internalSteam += (ventSteam - _internalSteam) * k;
      if (_burning)
        _burning = false;
      // An open lid relieves the vessel — clear any building over-pressure.
      _overpressureSeconds = 0f;
      MarkDirty(true);
      return;
    }

    bool dirty = false;

    // --- Fuel: a lit coal pile in the firebox, gated by exhaust back-pressure. ---
    BlockPos fuelPos = BoilerBlock?.FuelWorldPos(Pos) ?? Pos;
    var pile = ba.GetBlockEntity(fuelPos) as BlockEntityCoalPile;
    bool fireOn =
      pile?.IsBurning == true
      && pile.inventory != null
      && pile.inventory.Count > 0
      && !pile.inventory[0].Empty;

    PipeNetwork? exhaustNet =
      BoilerBlock != null
        ? NetworkAt(BoilerBlock.PipeOutletWorldPos(Pos))
        : null;
    float exhaustPressure = exhaustNet?.State?.Pressure ?? 0f;
    bool draughtBlocked =
      exhaustPressure >= PpexValues.ExhaustMaxOutputPressure;

    bool burning = fireOn && !draughtBlocked;
    if (burning != _burning)
    {
      _burning = burning;
      dirty = true;
    }

    float massFactor = Math.Max(
      1f,
      _waterVolume / Math.Max(1f, PpexValues.BoilerThermalMassReference)
    );
    float heatRate = PpexValues.BoilerHeatingSpeed / massFactor;

    // --- Vessel heat / cool (thermal mass scales both). ---
    float target = burning ? MaxTemperature : 20f;
    _internalTemperature += (target - _internalTemperature) * heatRate * dt;

    if (burning)
      ConsumeFuel(pile!, dt);

    // Water tracks the vessel temperature (capped at it); higher starting temp
    // (hot reclaimed water) reaches the boiling point much sooner.
    if (_waterVolume > 0f)
    {
      _waterTemperature +=
        (_internalTemperature - _waterTemperature) * heatRate * dt * 2f;
      _waterTemperature = Math.Min(_waterTemperature, _internalTemperature);
    }

    // --- Water in from the DOWN network (equilibrate boiler water temperature). ---
    PipeNetwork? waterNet = NetworkAt(Pos.DownCopy());
    if (waterNet != null && _waterVolume < WaterCapacity)
    {
      float want = Math.Min(
        WaterCapacity - _waterVolume,
        PpexValues.BoilerWaterPerSteam * SteamPerSecond * dt
      );
      float got = waterNet.TryConsumeLiquid(want, ba);
      if (got > 0f)
      {
        float incoming = waterNet.State?.WaterTemperature ?? 20f;
        float total = _waterVolume + got;
        _waterTemperature =
          (_waterVolume * _waterTemperature + got * incoming) / total;
        _waterVolume = total;
        dirty = true;
      }
    }

    // --- Boil: water at/above the boiling point becomes internal steam. ---
    if (
      _waterTemperature >= PpexValues.BoilingPoint
      && _waterVolume > 0f
      && _internalSteam < MaxInternalSteam
    )
    {
      float steam = Math.Min(
        SteamPerSecond * dt,
        MaxInternalSteam - _internalSteam
      );
      float waterNeeded = steam * PpexValues.BoilerWaterPerSteam;
      if (waterNeeded > _waterVolume)
      {
        waterNeeded = _waterVolume;
        steam = waterNeeded / PpexValues.BoilerWaterPerSteam;
      }
      _waterVolume -= waterNeeded;
      _internalSteam += steam;
      dirty = true;
    }

    // --- Steam out through the linked outlet (choked at the variant's max pressure). ---
    if (_internalSteam > 0f && OutletPos != null)
    {
      // The outlet is a fixed connector, not a network node — the steam network
      // lives in the cell on the other side of its connector face.
      BlockPos? steamNetPos = ba.GetBlock(OutletPos)
        is BlockBoilerSteamOutlet outlet
        ? OutletPos.AddCopy(outlet.ConnectorFace)
        : null;
      PipeNetwork? steamNet =
        steamNetPos != null ? NetworkAt(steamNetPos) : null;
      if (steamNet != null)
      {
        float before = steamNet.State?.CurrentVolume ?? 0f;
        steamNet.TryProduceGas(
          _internalSteam,
          _internalTemperature,
          "Steam",
          ba,
          maxOutputPressure: MaxOutputPressure,
          sourcePos: steamNetPos
        );
        float after = steamNet.State?.CurrentVolume ?? 0f;
        float accepted = Math.Max(0f, after - before);
        if (accepted > 0f)
        {
          _internalSteam = Math.Max(0f, _internalSteam - accepted);
          dirty = true;
        }
      }
    }

    // --- Exhaust out through the gas outlet (only while the draught isn't blocked). ---
    if (burning && exhaustNet != null && !draughtBlocked)
      exhaustNet.TryProduceGas(
        SteamPerSecond * dt,
        _internalTemperature * 0.6f,
        "Exhaust",
        ba,
        maxOutputPressure: PpexValues.ExhaustMaxOutputPressure,
        sourcePos: BoilerBlock!.PipeOutletWorldPos(Pos)
      );

    // --- Over-pressure explosion. ---
    // Danger when the steam buffer is full (can't make more) yet the fire keeps
    // boiling water with nowhere to vent: the outlet is choked or missing. Sustained
    // for too long, the vessel ruptures. Venting (lid) or any steam offtake resets it.
    bool steamFull = _internalSteam >= MaxInternalSteam - 0.01f;
    bool stillBoiling =
      burning
      && _waterTemperature >= PpexValues.BoilingPoint
      && _waterVolume > 0f;
    if (steamFull && stillBoiling)
    {
      _overpressureSeconds += dt;
      if (_overpressureSeconds >= PpexValues.BoilerOverpressureSeconds)
      {
        Explode();
        return;
      }
      dirty = true;
    }
    else if (_overpressureSeconds > 0f)
    {
      _overpressureSeconds = 0f;
      dirty = true;
    }

    if (dirty)
      MarkDirty(true);
  }

  /// <summary>
  /// Draws fuel from the firebox pile faster than a free pile would burn
  /// (<see cref="PpexValues.BoilerCoalBurnRateMultiplier"/>); the boiler owns the burn.
  /// </summary>
  private void ConsumeFuel(BlockEntityCoalPile pile, float dt)
  {
    _fuelAccum += dt * PpexValues.BoilerCoalBurnRateMultiplier;
    // One fuel item every ~8 seconds of accumulated (accelerated) burn.
    while (_fuelAccum >= 8f)
    {
      _fuelAccum -= 8f;
      if (
        pile.inventory == null
        || pile.inventory.Count == 0
        || pile.inventory[0].Empty
      )
        break;
      pile.inventory[0].TakeOut(1);
      pile.inventory[0].MarkDirty();
      pile.MarkDirty(true);
    }
  }

  private void Explode()
  {
    BlockPos pos = Pos.Copy();
    var world = Api.World;

    // Drop a fraction of the build materials (the boiler item) and clear the structure.
    if (BoilerBlock != null)
    {
      ItemStack[] drops = Block.GetDrops(world, pos, null, 0.2f) ?? [];
      foreach (var ds in drops)
        world.SpawnItemEntity(ds, pos.ToVec3d().Add(0.5, 0.5, 0.5));
      BoilerBlock.RemoveStructure(world, pos);
    }
    world.BlockAccessor.SetBlock(0, pos);

    // Blast: shatter blocks within the radius (sphere), no drops, keeping the
    // hardest (bedrock-like) blocks intact.
    int r = PpexValues.BoilerExplosionRadius;
    int r2 = r * r;
    var ba = world.BlockAccessor;
    for (int dx = -r; dx <= r; dx++)
    for (int dy = -r; dy <= r; dy++)
    for (int dz = -r; dz <= r; dz++)
    {
      if (dx * dx + dy * dy + dz * dz > r2)
        continue;
      BlockPos p = pos.AddCopy(dx, dy, dz);
      Block b = ba.GetBlock(p);
      if (b.BlockId == 0 || b.Resistance > 9000f)
        continue;
      ba.BreakBlock(p, null, 0f);
    }
  }

  #endregion

  #region Lid + manual fill

  /// <summary>Toggles the manual-access lid (sprint + RMB on the boiler).</summary>
  public void ToggleLid()
  {
    LidOpen = !LidOpen;
    MarkDirty(true);
  }

  /// <summary>
  /// Pours water from a held liquid container into the boiler (sneak + RMB while the
  /// lid is open), equilibrating the water temperature. The kickstart before the pump.
  /// </summary>
  public void TryManualFill(IPlayer byPlayer, ItemSlot slot)
  {
    if (slot.Itemstack?.Collectible is not BlockLiquidContainerBase cont)
      return;

    ItemStack? content = cont.GetContent(slot.Itemstack);
    if (content?.Collectible?.Code?.Path?.Contains("water") != true)
      return;

    // The boiler pool is m³; vanilla liquid containers are metered in litres.
    const float litresPerM3 = 1000f;
    float spaceLitres = (WaterCapacity - _waterVolume) * litresPerM3;
    if (spaceLitres <= 1f)
      return;

    // Take up to 50 L (or the space left) from the container, then convert to m³.
    int take = (int)Math.Min(50f, spaceLitres);
    var taken = cont.TryTakeContent(slot.Itemstack, take);
    float addedLitres = taken?.StackSize ?? 0;
    if (addedLitres <= 0f)
      return;

    float added = addedLitres / litresPerM3;
    float contentTemp = content.Collectible.GetTemperature(Api.World, content);
    float total = _waterVolume + added;
    _waterTemperature =
      (_waterVolume * _waterTemperature + added * Math.Max(20f, contentTemp))
      / total;
    _waterVolume = total;
    slot.MarkDirty();
    MarkDirty(true);
  }

  #endregion

  #region Client rendering + particles

  // The vessel renders water and emits steam across a per-variant footprint box,
  // expressed in the block's visual frame and rotated by the shape's rotateY.

  private float ShapeRotationRad =>
    (float)((Block.Shape?.rotateY ?? 0f) * Math.PI / 180.0);

  /// <summary>
  /// In-vessel water-surface footprint in 0-16 pixel space (block-local, rotated by
  /// the shape at render time), read from the block's <c>waterRendererBox</c> attribute
  /// (a deeper box for the longer Lancashire vessel, shorter for the Cornish). Falls
  /// back to a 3-deep box.
  /// </summary>
  protected virtual Cuboidf[] WaterRendererBoxes
  {
    get
    {
      var node = Block.Attributes?["waterRendererBox"];
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

  private void InitWaterRenderer(ICoreClientAPI capi)
  {
    _waterRenderer = new BoilerWaterRenderer(
      Pos,
      capi,
      WaterRendererBoxes,
      ShapeRotationRad,
      fillStartY: 0f,
      fillHeightLevels: 16f // water rises across y[0,1].
    );
    capi.Event.RegisterRenderer(_waterRenderer, EnumRenderStage.OIT);
  }

  private void OnClientTick(float dt)
  {
    if (_waterRenderer != null)
    {
      _waterRenderer.FillRatio =
        WaterCapacity > 0f
          ? GameMath.Clamp(_waterVolume / WaterCapacity, 0f, 1f)
          : 0f;
      _waterRenderer.Temperature = _waterTemperature;
    }

    // Steam plume above the water while actively boiling or venting through the lid.
    bool venting = LidOpen && _internalTemperature > 70f;
    bool boiling =
      _burning && _waterTemperature >= PpexValues.BoilingPoint - 5f;
    if (boiling || venting)
      SpawnSteamParticles(venting);
  }

  /// <summary>Rotates a block-local offset by the shape's rotateY around the cell centre.</summary>
  private Vec3d RotateLocal(double x, double y, double z)
  {
    double rad = ShapeRotationRad;
    double cx = x - 0.5;
    double cz = z - 0.5;
    double cos = Math.Cos(rad);
    double sin = Math.Sin(rad);
    double rx = cx * cos - cz * sin + 0.5;
    double rz = cx * sin + cz * cos + 0.5;
    return new Vec3d(Pos.X + rx, Pos.Y + y, Pos.Z + rz);
  }

  private void SpawnSteamParticles(bool venting)
  {
    if (Api is not ICoreClientAPI)
      return;

    var rnd = Api.World.Rand;
    int count = venting ? 6 : 2;
    for (int i = 0; i < count; i++)
    {
      // Local steam box x[-1,1] y[1,2] z[0,3]; sample, then rotate to world.
      double lx = -1.0 + rnd.NextDouble() * 2.0;
      double ly = 1.0 + rnd.NextDouble() * 1.0;
      double lz = rnd.NextDouble() * 3.0;
      Vec3d p = RotateLocal(lx, ly, lz);

      var particles = new SimpleParticleProperties(
        1,
        1,
        ColorUtil.ToRgba(120, 230, 230, 235),
        p,
        p.AddCopy(0.0, 0.5, 0.0),
        new Vec3f(-0.05f, 0.1f, -0.05f),
        new Vec3f(0.05f, 0.4f, 0.05f),
        venting ? 2.0f : 1.2f,
        -0.02f,
        0.3f,
        0.8f,
        EnumParticleModel.Quad
      )
      {
        OpacityEvolve = new EvolvingNatFloat(
          EnumTransformFunction.LINEAR,
          -180f
        ),
        SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1.5f),
        ShouldDieInLiquid = true,
      };
      Api.World.SpawnParticles(particles);
    }
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("internalTemp", _internalTemperature);
    tree.SetFloat("waterTemp", _waterTemperature);
    tree.SetFloat("waterVolume", _waterVolume);
    tree.SetFloat("internalSteam", _internalSteam);
    tree.SetBool("lidOpen", LidOpen);
    tree.SetBool("burning", _burning);
    tree.SetFloat("overpressure", _overpressureSeconds);
    if (OutletPos != null)
    {
      tree.SetInt("outletX", OutletPos.X);
      tree.SetInt("outletY", OutletPos.Y);
      tree.SetInt("outletZ", OutletPos.Z);
    }
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _internalTemperature = tree.GetFloat("internalTemp", 20f);
    _waterTemperature = tree.GetFloat("waterTemp", 20f);
    _waterVolume = tree.GetFloat("waterVolume");
    _internalSteam = tree.GetFloat("internalSteam");
    LidOpen = tree.GetBool("lidOpen");
    _burning = tree.GetBool("burning");
    _overpressureSeconds = tree.GetFloat("overpressure");
    OutletPos = tree.HasAttribute("outletX")
      ? new BlockPos(
        tree.GetInt("outletX"),
        tree.GetInt("outletY"),
        tree.GetInt("outletZ")
      )
      : null;
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  )
  {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed)
      return;

    if (!StructureComplete)
    {
      // _structure is loaded on the server monitor tick; ensure it is loaded here so
      // the client's look-at HUD can still report how many blocks are missing.
      UpdateStructureRotation();
      int missing = _structure?.InCompleteBlockCount(Api.World, Pos) ?? 0;
      dsc.AppendLine(Lang.Get("ppex:structure-incomplete-count", missing));
      return;
    }

    dsc.AppendLine(
      Lang.Get(
        "ppex:boiler-info-state",
        _internalTemperature,
        _waterVolume,
        _waterTemperature
      )
    );
    dsc.AppendLine(Lang.Get("ppex:boiler-info-pressure", InternalPressure));
    if (LidOpen)
      dsc.AppendLine(Lang.Get("ppex:boiler-info-lidopen"));
    else if (_burning)
      dsc.AppendLine(Lang.Get("ppex:boiler-info-firing"));

    if (_overpressureSeconds > 0f)
      dsc.AppendLine(
        Lang.Get(
          "ppex:boiler-info-overpressure",
          Math.Max(
            0f,
            PpexValues.BoilerOverpressureSeconds - _overpressureSeconds
          )
        )
      );
  }

  #endregion
}

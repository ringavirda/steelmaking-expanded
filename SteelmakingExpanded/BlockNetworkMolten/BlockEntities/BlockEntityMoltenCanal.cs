using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten.BlockEntities;

/// <summary>
/// Block entity for all molten-canal blocks. Each block is a self-contained
/// <em>cell</em> that holds its own liquid metal (amount, type, temperature); the
/// owning <see cref="MoltenNetwork"/> only provides connectivity and drives the
/// per-tick cell-to-cell flow and cooling. A cell solidifies on its own when its
/// metal drops below the melting point, blocking flow until chiselled or broken.
/// </summary>
[EntityRegister]
public class BlockEntityMoltenCanal : BlockEntityNetworkNode
{
  #region Network
  public override string NetworkType
  {
    get => "molten";
    set { }
  }

  /// <summary>This cell's metal capacity, in units (from the block's <c>maxUnits</c> attribute).</summary>
  public virtual int MaxUnitCapacity => SmexValues.CanalDefaultUnitCapacity;

  /// <summary>Units of liquid (or, once latched, solidified) metal held by this cell.</summary>
  public int CellAmount { get; protected set; }

  /// <summary>Full code of the metal in this cell, e.g. "game:ingot-iron"; empty when empty.</summary>
  public string CellMetalType { get; protected set; } = "";

  /// <summary>This cell's metal temperature (°C), updated from <see cref="_cellMetalStack"/> each tick.</summary>
  public float CellTemperature => _cellTemperature;

  // Server-side temperature carrier: an ItemStack so VS applies time-based
  // cooling. Null on clients and when the cell is empty; rebuilt lazily on load.
  private ItemStack? _cellMetalStack;
  private float _cellTemperature;

  // Client-only predicted fill from in-flight metal, for instant pour feedback.
  private float _pendingFillAmount;

  /// <summary>Whether this cell's metal has solidified.</summary>
  public bool Solidified { get; protected set; } = false;

  /// <summary>
  /// Whether this canal has been clay-sealed into a separator. A sealed canal severs
  /// the network at its position (acts as a manual valve) and renders capped ends on
  /// both of its connector faces.
  /// </summary>
  public bool Sealed { get; protected set; } = false;

  /// <summary>
  /// A sealed node severs connectivity at its position (manual valve), and so does
  /// a solidified one — a hardened cell must not pass metal or pull freshly placed
  /// neighbours into itself. Clear it with a chisel + hammer
  /// (see <see cref="ClearSolidified"/>) or break it to restore flow.
  /// </summary>
  public override bool IsConnectionBroken() => Sealed || Solidified;

  /// <summary>Whether this cell currently holds liquid (not solidified) metal.</summary>
  public bool HasMoltenMetal => !Solidified && CellAmount > 0f;

  /// <summary>
  /// Whether this cell's metal has cooled enough to be chiselled out — fully
  /// hardened below 0.3 × its melting point. A just-solidified cell (below the
  /// melting point yet still glowing hot) already blocks flow, but is too hot to
  /// chip out until it reaches this point. Works on both sides (melting point is
  /// resolved from <see cref="CellMetalType"/>, which is synced).
  /// </summary>
  public bool IsHardened
  {
    get
    {
      if (Api?.World == null || CellAmount <= 0 || CellMetalType.Length == 0)
        return false;
      Item? item = Api.World.GetItem(new AssetLocation(CellMetalType));
      if (item == null)
        return false;
      float meltPoint = MoltenMetal.MeltingPointOf(
        Api.World,
        new ItemStack(item)
      );
      return _cellTemperature < MoltenMetal.HardenedThreshold * meltPoint;
    }
  }

  #region Incandescent block light
  /// <summary>
  /// Block-light value (0–24) this cell emits from its hot metal (the shared
  /// <see cref="MoltenMetal.GlowLevel"/> scale). Read by
  /// <see cref="Blocks.BlockMoltenCanal.GetLightHsv"/>; 0 when empty or cool.
  /// Liquid or solidified — a freshly hardened cell still glows until it cools.
  /// </summary>
  public byte GlowLightLevel =>
    CellAmount > 0 ? MoltenMetal.GlowLevel(_cellTemperature) : (byte)0;

  /// <summary>
  /// Re-lights the block when the emitted glow level has shifted from
  /// <paramref name="oldGlow"/>. The block id doesn't change, so the engine won't
  /// relight on its own — we nudge it via <c>MarkBlockDirty</c> exactly like the
  /// heat sink does.
  /// </summary>
  private void RelightIfGlowChanged(byte oldGlow)
  {
    if (Api != null && GlowLightLevel != oldGlow)
      Api.World.BlockAccessor.MarkBlockDirty(Pos);
  }
  #endregion

  /// <summary>
  /// Whether this cell latches <see cref="Solidified"/> when its metal cools below
  /// the melting point. Plain canal runs do (they clog and must be chiselled);
  /// functional fittings (start, tap, mold pedestal) are sinks/sources, not
  /// storage, so they override this to keep passing metal even when it has cooled.
  /// </summary>
  protected virtual bool SolidifiesWhenCold => true;

  /// <summary>
  /// Seals or unseals this canal, then re-registers the node so the network graph
  /// splits around the seal (<c>RemoveNode</c> runs fracture detection) or rejoins
  /// the two runs when the seal is removed (<c>AddNode</c> merges the neighbours).
  /// </summary>
  public void SetSealed(bool sealedState)
  {
    if (Sealed == sealedState)
      return;
    Sealed = sealedState;

    ResyncNetworkNode();
    RefreshOpenConnectorFaces();
    MarkDirty(true);
  }

  /// <summary>
  /// Re-walks the network graph at this position so a change to
  /// <see cref="IsConnectionBroken"/> (seal, tap close, …) splits or rejoins the
  /// run immediately instead of waiting for the next placement/break.
  /// <c>RemoveNode</c> runs fracture detection; <c>AddNode</c> re-merges (or, for a
  /// now-broken node, isolates it). Server-side only.
  /// </summary>
  protected void ResyncNetworkNode()
  {
    if (
      Api?.Side == EnumAppSide.Server
      && NetworkSystem != null
      && Api.World?.BlockAccessor is { } ba
    )
    {
      NetworkSystem.RemoveNode(ba, Pos);
      NetworkSystem.AddNode(ba, Pos, NetworkType);
    }
  }
  #endregion

  #region Per-cell metal API
  /// <summary>
  /// Pushes up to <paramref name="amount"/> units of <paramref name="metal"/> into
  /// this cell, temperature-averaging with any metal already present (same type
  /// only). Returns the amount accepted. Server-side.
  /// </summary>
  public int PushMetal(int amount, ItemStack metal, IWorldAccessor world) =>
    PushMetalRaw(
      amount,
      metal.Collectible.Code.ToString(),
      metal.Collectible.GetTemperature(world, metal),
      world
    );

  internal int PushMetalRaw(
    int amount,
    string type,
    float temperature,
    IWorldAccessor world
  )
  {
    if (Solidified || type.Length == 0)
      return 0;
    if (CellAmount > 0f && CellMetalType != type)
      return 0;

    var accepted = Math.Min(amount, MaxUnitCapacity - CellAmount);
    if (accepted <= 0f)
      return 0;

    Item? item = world.GetItem(new AssetLocation(type));
    if (item == null)
      return 0;

    byte oldGlow = GlowLightLevel;
    float existingTemp = CellAmount > 0f ? _cellTemperature : temperature;
    float total = CellAmount + accepted;
    float newTemp =
      total > 0f
        ? (CellAmount * existingTemp + accepted * temperature) / total
        : temperature;

    if (_cellMetalStack == null || CellMetalType != type)
      _cellMetalStack = new ItemStack(item, 1);

    SetStackTemperature(world, newTemp);
    CellAmount += accepted;
    CellMetalType = type;
    _cellTemperature = newTemp;
    RelightIfGlowChanged(oldGlow);
    MarkDirty();
    return accepted;
  }

  /// <summary>Removes up to <paramref name="amount"/> liquid units from this cell. Returns the amount drained. Server-side.</summary>
  public int DrainMetal(int amount)
  {
    if (Solidified || CellAmount <= 0f)
      return 0;

    byte oldGlow = GlowLightLevel;
    var actual = Math.Min(amount, CellAmount);
    CellAmount -= actual;
    if (CellAmount <= 0.0001f)
      EmptyCell();

    RelightIfGlowChanged(oldGlow);
    MarkDirty();
    return actual;
  }

  private void EmptyCell()
  {
    CellAmount = 0;
    CellMetalType = "";
    _cellMetalStack = null;
    _cellTemperature = 0f;
  }

  private void SetStackTemperature(IWorldAccessor world, float temp)
  {
    if (_cellMetalStack == null)
      return;
    MoltenMetal.SetTemperature(world, _cellMetalStack, temp);
    MoltenMetal.SetCooldownSpeed(
      _cellMetalStack,
      SmexValues.MoltenCooldownSpeed
    );
  }

  /// <summary>
  /// Raises this cell's temperature toward <paramref name="incomingTemp"/> without
  /// adding any volume. Models hot metal poured over an already-full cell: the pour
  /// keeps bathing the surface, so a continuously-fed fitting stays molten instead
  /// of cooling to a solid plug once it can no longer accept more metal. Returns
  /// true if the temperature was raised. Server-side.
  /// </summary>
  internal bool SoakHeat(IWorldAccessor world, float incomingTemp)
  {
    if (
      CellAmount <= 0f
      || _cellMetalStack == null
      || incomingTemp <= _cellTemperature + 1f
    )
      return false;

    byte oldGlow = GlowLightLevel;
    _cellTemperature = incomingTemp;
    SetStackTemperature(world, _cellTemperature);
    RelightIfGlowChanged(oldGlow);
    MarkDirty();
    return true;
  }

  /// <summary>Rebuilds the server temperature carrier after a world load (To/FromTreeAttributes only persist type + temperature).</summary>
  internal void EnsureMetalStack(IWorldAccessor world)
  {
    if (
      _cellMetalStack != null
      || CellMetalType.Length == 0
      || CellAmount <= 0f
    )
      return;

    Item? item = world.GetItem(new AssetLocation(CellMetalType));
    if (item == null)
      return;
    _cellMetalStack = new ItemStack(item, 1);
    SetStackTemperature(world, _cellTemperature);
  }

  /// <summary>
  /// Server per-tick thermal update: refreshes the displayed temperature from the
  /// VS time-based decay and latches <see cref="Solidified"/> once the metal drops
  /// below its melting point. Driven by <see cref="MoltenNetwork.OnTick"/>.
  /// </summary>
  internal void UpdateThermal(IWorldAccessor world)
  {
    if (CellAmount <= 0f || _cellMetalStack == null)
      return;

    byte oldGlow = GlowLightLevel;
    float temp = MoltenMetal.GetTemperature(world, _cellMetalStack);
    float meltPoint = MoltenMetal.MeltingPointOf(world, _cellMetalStack);

    bool changed = false;
    bool retesselate = false;
    if (Math.Abs(_cellTemperature - temp) >= 1f)
    {
      _cellTemperature = temp;
      changed = true;
    }
    if (SolidifiesWhenCold && !Solidified && temp < meltPoint)
    {
      Solidified = true;
      changed = true;
      retesselate = true;
    }

    if (changed)
      MarkDirty(retesselate);

    RelightIfGlowChanged(oldGlow);
  }
  #endregion

  #region Solidified clearing / drops
  /// <summary>
  /// Server-side: chips the hardened metal out of <em>this cell only</em> — returns
  /// the recoverable solid-metal drop, empties the cell and lifts its solidified
  /// latch, then rebuilds so the freed cell rejoins the run. Returns <c>null</c>
  /// off-server or when this cell isn't solidified.
  /// </summary>
  public ItemStack? ClearSolidified()
  {
    if (Api?.Side != EnumAppSide.Server || !Solidified || !IsHardened)
      return null;

    ItemStack? recovered = GetSolidifiedDrop(Api.World);
    EmptyCell();
    Solidified = false;
    MarkDirty(true);

    // No longer broken — rebuild so this cell re-merges with its neighbours.
    if (NetworkSystem != null && Api.World?.BlockAccessor is { } ba)
      NetworkSystem.RebuildFromRoot(ba, Pos, NetworkType);

    return recovered;
  }

  /// <summary>Whether breaking this canal would let still-liquid metal spill out.</summary>
  public bool WouldSpillOnRemoval() => !Solidified && CellAmount > 0f;

  /// <summary>Returns the solid metal-bit drop for this solidified cell, or <c>null</c> if there's nothing to drop.</summary>
  public ItemStack? GetSolidifiedDrop(IWorldAccessor world)
  {
    if (!Solidified || CellAmount <= 0f || CellMetalType.Length == 0)
      return null;

    int count = Math.Max(1, (int)(CellAmount / 5f));
    var solidLoc = MoltenNetwork.SolidDropLocation(
      new AssetLocation(CellMetalType)
    );
    Item? item = world.GetItem(solidLoc);
    if (item == null)
      return null;

    var drop = new ItemStack(item, count);
    MoltenMetal.SetTemperature(world, drop, _cellTemperature);
    return drop;
  }
  #endregion

  #region Tesselation
  /// <summary>Faces with no canal neighbour, capped with an end-piece mesh; <c>null</c> when none.</summary>
  public BlockFacing[]? OpenConnectorFaces { get; set; }
  private readonly Dictionary<BlockFacing, MeshData> _cachedEndingMeshes = [];
  private MeshData? _baseMesh;

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  )
  {
    // Recompute open faces from the current neighbours every tessellation rather
    // than trusting the cached value. On chunk load the cache is populated before
    // adjacent blocks (especially across a chunk boundary) are available, capping
    // a face that is actually connected. The engine re-tessellates edge blocks
    // once the neighbour chunk arrives, but that re-bakes the same stale cache —
    // so the wrong cap persists until a network broadcast (e.g. pouring metal)
    // happens to refresh it. Refreshing here lets that automatic re-tessellation
    // self-correct without needing a broadcast.
    RefreshOpenConnectorFaces();

    var baseShapeLoc = new AssetLocation(
      $"smex:shapes/molten/canal/{Block?.Variant["type"]}.json"
    );
    Shape baseShape = Api.Assets.Get<Shape>(baseShapeLoc);
    if (baseShape != null)
    {
      tesselator.TesselateShape(Block, baseShape, out _baseMesh);
      if (Block?.Shape != null)
      {
        float rotX = Block.Shape.rotateX * GameMath.DEG2RAD;
        float rotY = Block.Shape.rotateY * GameMath.DEG2RAD;
        float rotZ = Block.Shape.rotateZ * GameMath.DEG2RAD;

        if (rotX != 0 || rotY != 0 || rotZ != 0)
        {
          Vec3f center = new(0.5f, 0.5f, 0.5f);
          _baseMesh.Rotate(center, rotX, rotY, rotZ);
        }
      }
    }
    if (_baseMesh != null)
      mesher.AddMeshData(_baseMesh);

    // Add rotated ending meshes for open connector faces.
    if (OpenConnectorFaces != null)
    {
      foreach (var face in OpenConnectorFaces)
      {
        if (!_cachedEndingMeshes.TryGetValue(face, out var endMesh))
        {
          endMesh = MoltenMeshes.TesselateEndCap(Api, tesselator, Block!, face);
          if (endMesh == null)
            continue;
          _cachedEndingMeshes.Add(face, endMesh);
        }
        mesher.AddMeshData(endMesh);
      }
    }

    base.OnTesselation(mesher, tesselator);
    return true;
  }
  #endregion

  #region Rendering
  protected MoltenRenderer? _renderer;
  private string? _cachedMetalType;
  private ItemStack? _cachedMetalStack;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);

    RefreshOpenConnectorFaces();

    if (api.Side == EnumAppSide.Client)
    {
      InitRenderer((ICoreClientAPI)api);
      UpdateRenderer();
    }
  }

  private void RefreshOpenConnectorFaces()
  {
    if (
      Api?.World?.BlockAccessor == null
      || Block is not BlockNetworkNode netBlock
    )
    {
      OpenConnectorFaces = null;
      return;
    }

    // A sealed canal cuts itself off from the network, so every connector face is a
    // capped end regardless of its neighbours — that capping is the visible seal.
    if (Sealed)
    {
      BlockFacing[]? faces = netBlock.GetConnectorFaces();
      OpenConnectorFaces = faces is { Length: > 0 } ? faces : null;
      return;
    }

    if (NetworkSystem == null)
    {
      OpenConnectorFaces = null;
      return;
    }

    BlockFacing[] open = NetworkSystem.GetOpenConnectorFaces(
      Api.World.BlockAccessor,
      Pos,
      netBlock
    );
    OpenConnectorFaces = open.Length > 0 ? open : null;
  }

  /// <summary>Creates the molten-fill renderer from the block's fill-quad attributes. Override to customise the fill geometry.</summary>
  protected virtual void InitRenderer(ICoreClientAPI capi)
  {
    if (Block is not BlockMoltenCanal)
      return;

    Cuboidf[] boxes = FillQuads.ReadBoxes(
      Block,
      "fillQuadsByLevel",
      new Cuboidf(7f, 0f, 0f, 9f, 16f, 16f)
    );
    float fillStartY = FillQuads.ReadStartY(Block, "fillStart", 2f);
    float fillHeightLevels = FillQuads.ReadHeightLevels(
      Block,
      "fillHeight",
      12f
    );
    float rotY = (Block.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;

    _renderer = new MoltenRenderer(
      Pos,
      capi,
      boxes,
      rotY,
      fillStartY,
      fillHeightLevels
    );
    capi.Event.RegisterRenderer(_renderer, EnumRenderStage.Opaque);
  }

  /// <summary>Pushes this cell's fill ratio, temperature and metal stack into the renderer. Override to add custom render state.</summary>
  protected virtual void UpdateRenderer()
  {
    if (_renderer == null)
      return;

    float displayAmount = CellAmount + _pendingFillAmount;
    _renderer.FillRatio =
      MaxUnitCapacity > 0 ? displayAmount / MaxUnitCapacity : 0f;
    _renderer.Temperature = _cellTemperature;

    if (CellMetalType != _cachedMetalType)
    {
      if (CellMetalType.Length == 0)
      {
        _cachedMetalStack = null;
        _cachedMetalType = "";
      }
      else
      {
        Item? item = Api.World.GetItem(new AssetLocation(CellMetalType));
        _cachedMetalStack = item != null ? new ItemStack(item) : null;
        // Only advance the cache key when the item resolved; leave stale to retry if not yet registered.
        if (item != null)
          _cachedMetalType = CellMetalType;
      }
    }
    _renderer.MetalStack = _cachedMetalStack;
  }

  /// <summary>Client-side: shows in-flight poured metal immediately, before the server confirms.</summary>
  public void ShowPendingFill(float amount)
  {
    _pendingFillAmount = amount;
    UpdateRenderer();
  }

  public override void OnBlockRemoved()
  {
    _renderer?.Dispose();
    _renderer = null;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    _renderer?.Dispose();
    _renderer = null;
    base.OnBlockUnloaded();
  }
  #endregion

  #region Serialization / info
  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("solidified", Solidified);
    tree.SetBool("sealed", Sealed);
    tree.SetInt("cellAmount", CellAmount);
    tree.SetString("cellMetalType", CellMetalType);
    tree.SetFloat("cellTemperature", _cellTemperature);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    byte oldGlow = GlowLightLevel;
    Solidified = tree.GetBool("solidified");
    Sealed = tree.GetBool("sealed");
    CellAmount = tree.GetInt("cellAmount");
    CellMetalType = tree.GetString("cellMetalType", "");
    _cellTemperature = tree.GetFloat("cellTemperature");
    // _cellMetalStack is rebuilt lazily server-side in EnsureMetalStack.

    // Invariant: an empty cell is never solidified (also scrubs phantom solid
    // flags from pre-per-cell saves, whose metal didn't carry over).
    if (CellAmount <= 0f)
    {
      Solidified = false;
      CellMetalType = "";
    }

    // Authoritative state has arrived — drop any client-predicted pour fill.
    _pendingFillAmount = 0f;

    RefreshOpenConnectorFaces();
    UpdateRenderer();

    // Client received new authoritative state — re-light if the glow level moved.
    if (Api?.Side == EnumAppSide.Client)
      RelightIfGlowChanged(oldGlow);
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    if (Sealed)
      dsc.AppendLine(Lang.Get("smex:canal-sealed"));

    if (Solidified)
    {
      string solidMetalName = MoltenMetal.DisplayName(CellMetalType);
      dsc.AppendLine(
        Lang.Get(
          "smex:canal-solidified",
          CellAmount,
          MaxUnitCapacity,
          solidMetalName,
          _cellTemperature
        )
      );
      // Hot solid plug: still glowing, too hot to chip out. Tell the player to
      // wait for it to cool below the chisellable (hardened) threshold.
      dsc.AppendLine(
        Lang.Get(IsHardened ? "smex:canal-chiselready" : "smex:canal-cooling")
      );
      return;
    }

    if (CellAmount <= 0f)
    {
      dsc.AppendLine(Lang.Get("smex:canal-empty"));
    }
    else
    {
      string metalName = MoltenMetal.DisplayName(CellMetalType);
      dsc.AppendLine(
        Lang.Get(
          "smex:canal-content2",
          CellAmount,
          MaxUnitCapacity,
          metalName,
          _cellTemperature
        )
      );
    }
  }

  #endregion
}

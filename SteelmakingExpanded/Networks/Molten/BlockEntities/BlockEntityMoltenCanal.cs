using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using BlockNetworkLib;
using SteelmakingExpanded.Networks.Molten.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Molten.BlockEntities;

/// <summary>
/// Block entity for all molten-canal blocks. Caches the owning <c>MoltenNetwork</c>'s
/// fill/temperature/metal for rendering and HUD, tessellates capped ends on open
/// connectors, and produces solidified-metal drops when the canal is broken.
/// </summary>
public class BlockEntityMoltenCanal : BlockEntityNetworkNode
{
  #region Network
  public override string NetworkType
  {
    get => "molten";
    set { }
  }

  /// <summary>Default per-block canal capacity in units when no <c>maxUnits</c> attribute is set.</summary>
  public const int DefaultUnitCapacity = 20;

  /// <summary>This block's contribution to the network capacity, in units.</summary>
  public int MaxUnitCapacity { get; private set; } = DefaultUnitCapacity;

  /// <summary>Whether the metal in this canal has solidified (network must be rebuilt).</summary>
  public bool Solidified { get; protected set; } = false;

  /// <summary>
  /// Whether this canal has been clay-sealed into a separator. A sealed canal severs
  /// the network at its position (acts as a manual valve) and renders capped ends on
  /// both of its connector faces.
  /// </summary>
  public bool Sealed { get; protected set; } = false;

  /// <summary>Chance per unit that breaking a solidified canal drops a metal bit.</summary>
  public readonly float SolidifiedBitDropProbability = 0.2f;

  /// <summary>A sealed node severs network connectivity at its position.</summary>
  public override bool IsConnectionBroken() => Sealed;

  /// <summary>Whether this canal's network currently holds liquid (not solidified) metal.</summary>
  public bool HasMoltenMetal => !Solidified && _clientCurrentAmount > 0f;

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

    if (
      Api?.Side == EnumAppSide.Server
      && NetworkSystem != null
      && Api.World?.BlockAccessor is { } ba
    )
    {
      NetworkSystem.RemoveNode(ba, Pos);
      NetworkSystem.AddNode(ba, Pos, NetworkType);
    }

    RefreshOpenConnectorFaces();
    MarkDirty(true);
  }

  public override void OnNetworkUpdate(object? state)
  {
    base.OnNetworkUpdate(state);

    if (state is MoltenNetworkState netState)
    {
      _clientTemperature = netState.CurrentTemperature;
      _clientMetalType = netState.MetalType;
      _clientCurrentAmount = netState.CurrentAmount;
      _clientMaxAmount = netState.MaxAmount;
      Solidified = netState.Solidified;
    }
    else
    {
      _clientTemperature = 0f;
      _clientMetalType = "";
      _clientCurrentAmount = 0f;
    }
    _pendingFillAmount = 0f;

    RefreshOpenConnectorFaces();
    UpdateRenderer();
    MarkDirty(true);
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
        if (_cachedEndingMeshes.TryGetValue(face, out var cachedMesh))
        {
          mesher.AddMeshData(cachedMesh);
          continue;
        }
        var endShapeLoc = new AssetLocation(
          "smex:shapes/molten/canal/end.json"
        );
        var endShape = Api.Assets.Get<Shape>(endShapeLoc);
        if (endShape != null)
        {
          tesselator.TesselateShape(Block, endShape, out var endMesh);
          float rotX = 0f * GameMath.DEG2RAD;
          float rotZ = 0f * GameMath.DEG2RAD;

          float rotY =
            face.Index switch
            {
              BlockFacing.indexNORTH => 180f,
              BlockFacing.indexEAST => 90f,
              BlockFacing.indexWEST => 270f,
              _ => 0f,
            } * GameMath.DEG2RAD;

          Vec3f center = new(0.5f, 0.5f, 0.5f);
          endMesh.Rotate(center, rotX, rotY, rotZ);

          _cachedEndingMeshes.Add(face, endMesh);
          mesher.AddMeshData(endMesh);
        }
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

    MaxUnitCapacity =
      Block?.Attributes?["maxUnits"].AsInt(DefaultUnitCapacity)
      ?? DefaultUnitCapacity;
  }

  private void RefreshOpenConnectorFaces()
  {
    if (Api?.World?.BlockAccessor == null || Block is not BlockNetworkNode netBlock)
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

    Cuboidf[] boxes;
    var quadDefs = Block.Attributes?[
      "fillQuadsByLevel"
    ]?.AsObject<FillQuadDef[]>();
    if (quadDefs != null && quadDefs.Length > 0)
      boxes = quadDefs
        .Select(q => new Cuboidf(q.x1, 0f, q.z1, q.x2, 16f, q.z2))
        .ToArray();
    else
      boxes = [new Cuboidf(7f, 0f, 0f, 9f, 16f, 16f)];

    int fillStartPx = Block.Attributes?["fillStart"]?.AsInt(2) ?? 2;
    int fillHeightLevels = Block.Attributes?["fillHeight"]?.AsInt(12) ?? 12;
    float fillStartY = fillStartPx / 16f;
    float rotY = (float)((Block.Shape?.rotateY ?? 0f) * Math.PI / 180.0);

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

  /// <summary>Pushes the cached fill ratio, temperature and metal stack into the renderer. Override to add custom render state.</summary>
  protected virtual void UpdateRenderer()
  {
    if (_renderer == null)
      return;

    float displayAmount = _clientCurrentAmount + _pendingFillAmount;
    _renderer.FillRatio =
      _clientMaxAmount > 0f ? displayAmount / _clientMaxAmount : 0f;
    _renderer.Temperature = _clientTemperature;

    if (_clientMetalType != _cachedMetalType)
    {
      if (_clientMetalType.Length == 0)
      {
        _cachedMetalStack = null;
        _cachedMetalType = "";
      }
      else
      {
        Item? item = Api.World.GetItem(new AssetLocation(_clientMetalType));
        _cachedMetalStack = item != null ? new ItemStack(item) : null;
        // Only advance the cache key when the item resolved; leave stale to retry if not yet registered.
        if (item != null)
          _cachedMetalType = _clientMetalType;
      }
    }
    _renderer.MetalStack = _cachedMetalStack;
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

  #region Drop / sound helpers
  /// <summary>Whether breaking this canal would let still-liquid metal spill out.</summary>
  public bool WouldSpillOnRemoval() =>
    !Solidified
    && _clientCurrentAmount > 0
    && _clientCurrentAmount > (_clientMaxAmount - MaxUnitCapacity);

  /// <summary>Returns the solid metal-bit drop for a solidified canal, or <c>null</c> if there is nothing to drop.</summary>
  public ItemStack? GetSolidifiedDrop(IWorldAccessor world)
  {
    if (
      !Solidified
      || _clientCurrentAmount <= 0f
      || _clientMetalType.Length == 0
    )
      return null;

    int randLoss = Random.Shared.Next(3) * 5;
    float remaining = _clientCurrentAmount - randLoss;
    if (remaining <= 0f)
      return null;

    int count = Math.Max(1, (int)(remaining / 5f));
    var solidLoc = MoltenNetwork.SolidDropLocation(
      new AssetLocation(_clientMetalType)
    );
    Item? item = world.GetItem(solidLoc);
    if (item == null)
      return null;

    var drop = new ItemStack(item, count);
    drop.Collectible.SetTemperature(
      world,
      drop,
      _clientTemperature,
      delayCooldown: false
    );
    return drop;
  }
  #endregion

  #region Serialization Info
  protected float _clientCurrentAmount;
  protected float _clientMaxAmount;
  protected float _clientTemperature;

  // Full AssetLocation string, e.g. "game:ingot-iron". Empty when canal is empty.
  protected string _clientMetalType = "";
  protected float _pendingFillAmount;

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("solidified", Solidified);
    tree.SetBool("sealed", Sealed);
    tree.SetFloat("clientTemperature", _clientTemperature);
    tree.SetString("clientMetalType", _clientMetalType);
    tree.SetFloat("clientCurrentAmount", _clientCurrentAmount);
    tree.SetFloat("clientMaxAmount", _clientMaxAmount);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    Solidified = tree.GetBool("solidified");
    Sealed = tree.GetBool("sealed");
    _clientTemperature = tree.GetFloat("clientTemperature");

    // Migrate old "Iron"/"Steel"/"Slag" values to full AssetLocation strings.
    string rawType = tree.GetString("clientMetalType", "");
    _clientMetalType = rawType switch
    {
      "Iron" => "game:ingot-iron",
      "Steel" => "game:ingot-steel",
      "Slag" => "smex:slag",
      _ => rawType,
    };

    _clientCurrentAmount = tree.GetFloat("clientCurrentAmount");
    _clientMaxAmount = tree.GetFloat("clientMaxAmount");
    RefreshOpenConnectorFaces();
    UpdateRenderer();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    if (Sealed)
      dsc.AppendLine(Lang.Get("smex:canal-sealed"));

    if (Solidified)
    {
      dsc.AppendLine(Lang.Get("smex:canal-solidified"));
      return;
    }

    if (_clientCurrentAmount == 0)
    {
      dsc.AppendLine(Lang.Get("smex:canal-empty"));
    }
    else
    {
      string metalName = MetalDisplayName(_clientMetalType);
      dsc.AppendLine(
        Lang.Get(
          "smex:canal-content2",
          _clientCurrentAmount,
          _clientMaxAmount,
          metalName,
          _clientTemperature
        )
      );
    }
  }

  // "game:ingot-iron" → "Iron", "smex:slag" → "Slag", "" → "unknown"
  private static string MetalDisplayName(string metalItemCode)
  {
    if (metalItemCode.Length == 0)
      return Lang.Get("smex:metal-unknown");
    string path = new AssetLocation(metalItemCode).Path;
    string name = path.StartsWith("ingot-") ? path[6..] : path;
    return name.Length > 0 ? char.ToUpper(name[0]) + name[1..] : name;
  }
  #endregion
}

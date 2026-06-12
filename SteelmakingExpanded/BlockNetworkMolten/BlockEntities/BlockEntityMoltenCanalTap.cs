using System;
using System.Linq;
using System.Text;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten.BlockEntities;

/// <summary>
/// Block entity for the canal tap: pours the network's liquid metal into whatever
/// is parked beneath it — a molten barrel or a large tool mold (anvil, helve
/// hammer) — draining the network each tick while pouring is enabled.
/// </summary>
[EntityRegister]
public class BlockEntityMoltenCanalTap : BlockEntityMoltenCanal
{
  private bool _isPouring;

  /// <summary>Whether the tap is actively draining the network into its content.</summary>
  public bool IsPouring
  {
    get => _isPouring;
    private set
    {
      if (_isPouring == value)
        return;
      _isPouring = value;
      // Open/closed flips IsConnectionBroken, so re-walk the graph to sever the tap
      // from (or rejoin it to) the run. No-op off-server / before Initialize, where
      // base.Initialize's AddNode registers with the correct broken-state instead.
      ResyncNetworkNode();
    }
  }

  // The tap is a drain fitting — its cell must keep delivering to the parked
  // barrel/mold, so it never clogs like a plain canal run.
  protected override bool SolidifiesWhenCold => false;

  // A closed tap severs itself from the run (it's a single-connector leaf) so no
  // metal flows into its own cell — IsPouring otherwise only gates the tap's own
  // draining into parked content, leaving the cell to keep filling from the network.
  public override bool IsConnectionBroken() =>
    base.IsConnectionBroken() || !IsPouring;

  /// <summary> Canal tap by itself has low capacity. </summary>
  public override int MaxUnitCapacity =>
    (int)Math.Ceiling(SmexValues.CanalDefaultUnitCapacity / 2.0);

  #region Barrel content
  /// <summary>Whether a barrel is parked under the tap.</summary>
  public bool IsBarrel { get; set; } = false;

  /// <summary>Metal stored in the parked barrel, or <c>null</c>.</summary>
  public ItemStack? BarrelMetalContent { get; private set; }

  /// <summary>Units of metal in the parked barrel.</summary>
  public int BarrelCurrentUnits { get; private set; }

  /// <summary>Capacity of the parked barrel, in units.</summary>
  public int BarrelMaxUnits { get; private set; } =
    SmexValues.BarrelDefaultMaxUnits;
  #endregion

  #region Mold content (large molds only — anvil, helve hammer)
  /// <summary>Whether a large tool mold is parked under the tap.</summary>
  public bool IsMold { get; set; } = false;

  /// <summary>The parked mold item, or <c>null</c>.</summary>
  public ItemStack? MoldStack { get; private set; }

  /// <summary>Metal cast into the parked mold, or <c>null</c>.</summary>
  public ItemStack? MoldMetalContent { get; private set; }

  /// <summary>Units of metal in the parked mold.</summary>
  public int MoldCurrentUnits { get; private set; }

  /// <summary>The parked mold's capacity, in units.</summary>
  public int MoldMaxUnits { get; private set; } = SmexValues.MoldDefaultUnits;
  #endregion

  // Molds sit on the floor (bottom) of the tap block. Kept as a single knob so
  // the mold mesh and its molten-surface renderer stay in lockstep.
  private const float MoldMeshRaiseY = 0f;

  private MeshData? _barrelMesh;
  private MeshData? _moldMesh;
  private AssetLocation? _tessellatedMoldCode;
  private MeshData? _tapEndMesh;

  // Single renderer for whatever sits in the tap; rebuilt with the content's
  // own fill footprint whenever the content changes (barrel ⇄ mold ⇄ empty).
  private MoltenRenderer? _contentRenderer;
  private string? _contentRendererKey;
  private string? _cachedContentMetalType;
  private ItemStack? _cachedContentMetalStack;

  // Throttle for the looping molten-pour hiss while draining into the content.
  private long _lastDrainSoundMs;

  // Current network drain speed.
  private float _drainSpeed;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnServerTick, 1000);
    else
      // The parked barrel/mold metal keeps cooling after the pour stops
      // broadcasting (full / hardened), so refresh the surface glow on the client
      // from the stack's live temperature — otherwise it freezes at the last server
      // value and snaps cold on the next interaction.
      RegisterGameTickListener(_ => UpdateRenderer(), 1000);

    _drainSpeed = Block
      .Attributes["drainSpeed"]
      .AsFloat(SmexValues.CanalDefaultDrainSpeed);
  }

  public override void OnBlockRemoved()
  {
    _contentRenderer?.Dispose();
    _contentRenderer = null;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    _contentRenderer?.Dispose();
    _contentRenderer = null;
    base.OnBlockUnloaded();
  }

  #region Renderer

  protected override void UpdateRenderer()
  {
    base.UpdateRenderer();
    if (!IsPouring && _renderer != null)
      _renderer.FillRatio = 0f;
    UpdateContentRenderer();
  }

  private void UpdateContentRenderer()
  {
    if (Api is not ICoreClientAPI capi)
      return;

    Block? contentBlock = null;
    string? key = null;
    float raiseY = 0f;
    ItemStack? content = null;
    int currentUnits = 0;
    int maxUnits = 0;

    if (IsBarrel)
    {
      contentBlock = capi.World.GetBlock(
        new AssetLocation("smex:moltenbarrel")
      );
      key = "barrel";
      content = BarrelMetalContent;
      currentUnits = BarrelCurrentUnits;
      maxUnits = BarrelMaxUnits;
    }
    else if (IsMold && MoldStack?.Block != null)
    {
      contentBlock = MoldStack.Block;
      key = "mold:" + contentBlock.Code;
      raiseY = MoldMeshRaiseY;
      content = MoldMetalContent;
      currentUnits = MoldCurrentUnits;
      maxUnits = MoldMaxUnits;
    }

    EnsureContentRenderer(capi, key, contentBlock, raiseY);

    if (_contentRenderer == null)
      return;

    if (content == null || currentUnits <= 0 || maxUnits <= 0)
    {
      _contentRenderer.FillRatio = 0f;
      _contentRenderer.MetalStack = null;
      return;
    }

    _contentRenderer.FillRatio = (float)currentUnits / maxUnits;
    _contentRenderer.Temperature = content.Collectible.GetTemperature(
      Api.World,
      content
    );

    string metalCode = content.Collectible.Code.ToString();
    if (metalCode != _cachedContentMetalType)
    {
      _cachedContentMetalStack = new ItemStack(content.Collectible);
      _cachedContentMetalType = metalCode;
    }
    _contentRenderer.MetalStack = _cachedContentMetalStack;
  }

  private void EnsureContentRenderer(
    ICoreClientAPI capi,
    string? key,
    Block? block,
    float raiseY
  )
  {
    if (key == _contentRendererKey && _contentRenderer != null)
      return;

    _contentRenderer?.Dispose();
    _contentRenderer = null;
    _contentRendererKey = key;
    _cachedContentMetalType = null;
    _cachedContentMetalStack = null;

    if (key == null || block == null)
      return;

    Cuboidf[] boxes = ExtractFootprint(
      block,
      out float baseFillStartY,
      out float fillHeightLevels
    );
    // Rotate the molten-surface footprint to match how the content mesh is drawn.
    // For a parked MOLD, vanilla authors fillQuadsByLevel in the mold's FINAL
    // (already shape-rotated) world orientation, so they must NOT be re-rotated by
    // the mold's own Shape.rotateY (e.g. the anvil's 270 — doing so rendered the
    // metal surface perpendicular to the mold). The mold mesh only takes the tap's
    // facing rotation on top of its baked shape (see _moldMesh.Rotate in
    // OnTesselation, which uses this.Block.Shape.rotateY), so the footprint takes
    // exactly that and nothing else. The barrel uses its own (round, ≈0) shape.
    float rotY =
      (key.StartsWith("mold:") ? Block?.Shape?.rotateY : block.Shape?.rotateY)
      ?? 0f;
    rotY *= GameMath.DEG2RAD;

    _contentRenderer = new MoltenRenderer(
      Pos,
      capi,
      boxes,
      rotY,
      raiseY + baseFillStartY,
      fillHeightLevels
    );
    capi.Event.RegisterRenderer(_contentRenderer, EnumRenderStage.Opaque);
  }

  private static Cuboidf[] ExtractFootprint(
    Block block,
    out float fillStartY,
    out float fillHeightLevels
  )
  {
    fillStartY = FillQuads.ReadStartY(block, "fillStart", 1f);
    fillHeightLevels = FillQuads.ReadHeightLevels(block, "fillHeight", 8f);
    return FillQuads.ReadBoxes(
      block,
      "fillQuadsByLevel",
      new Cuboidf(4f, 0f, 4f, 12f, 16f, 12f)
    );
  }

  #endregion

  #region Barrel attach / detach

  /// <summary>Parks <paramref name="barrelStack"/> under the tap, adopting any metal it already holds.</summary>
  public void AddBarrel(ItemStack barrelStack)
  {
    (BarrelMetalContent, BarrelCurrentUnits) = MoltenContents.Read(
      barrelStack,
      MoltenContents.BarrelUnitsKey,
      Api.World
    );
    BarrelMaxUnits =
      barrelStack.Block?.Attributes?["maxUnits"].AsInt(
        SmexValues.BarrelDefaultMaxUnits
      ) ?? SmexValues.BarrelDefaultMaxUnits;
    IsBarrel = true;
    IsMold = false;
  }

  /// <summary>Removes the parked barrel and returns it, preserving its contents in <c>blockEntityAttributes</c>.</summary>
  public ItemStack RemoveBarrel()
  {
    IsBarrel = false;
    var barrelBlock = Api.World.GetBlock(
      new AssetLocation("smex:moltenbarrel")
    );
    var stack = new ItemStack(barrelBlock);
    MoltenContents.Write(
      stack,
      MoltenContents.BarrelUnitsKey,
      BarrelMetalContent,
      BarrelCurrentUnits
    );
    BarrelMetalContent = null;
    BarrelCurrentUnits = 0;
    BarrelMaxUnits = SmexValues.BarrelDefaultMaxUnits;
    return stack;
  }

  #endregion

  #region Mold attach / detach

  /// <summary>Parks <paramref name="itemStack"/> (a large mold) under the tap, adopting any metal it already holds.</summary>
  public void AddMold(ItemStack itemStack)
  {
    MoldStack = itemStack.Clone();
    MoldStack.StackSize = 1;

    (MoldMetalContent, MoldCurrentUnits) = MoltenContents.Read(
      itemStack,
      MoltenContents.MoldUnitsKey,
      Api.World
    );

    MoldMaxUnits =
      MoldStack.Block?.Attributes?["requiredUnits"].AsInt(
        SmexValues.MoldDefaultUnits
      ) ?? SmexValues.MoldDefaultUnits;
    IsMold = true;
    IsBarrel = false;
  }

  /// <summary>Removes the parked mold and returns it, preserving any cast metal in <c>blockEntityAttributes</c>.</summary>
  public ItemStack RemoveMold()
  {
    IsMold = false;
    var stack = MoldStack!.Clone();

    MoltenContents.Write(
      stack,
      MoltenContents.MoldUnitsKey,
      MoldMetalContent,
      MoldCurrentUnits
    );

    MoldStack = null;
    MoldMetalContent = null;
    MoldCurrentUnits = 0;
    MoldMaxUnits = SmexValues.MoldDefaultUnits;
    _moldMesh = null;
    _tessellatedMoldCode = null;
    return stack;
  }

  /// <summary>Whether a barrel or mold is currently parked under the tap.</summary>
  public bool HasContent => IsBarrel || IsMold;

  /// <summary>Toggles whether the tap drains the network into its parked content.</summary>
  public void TryTogglePouring()
  {
    IsPouring = !IsPouring; // setter re-walks the network on change
    ExSounds.Play(Api, Pos, ExSounds.Latch, 0.7f);
    MarkDirty(true);
  }

  #endregion

  #region Server tick: drain network into the current content

  private void OnServerTick(float dt)
  {
    // The tap drains its own cell (where the run delivers metal) into the parked
    // barrel or mold.
    if (!IsPouring || !HasMoltenMetal)
      return;

    // Barrels accept metal even when their contents have hardened (it re-melts);
    // molds stay gated — a hardened mold is a finished cast.
    if (IsBarrel)
    {
      var content = BarrelMetalContent;
      int drained = DrainInto(
        ref content,
        BarrelCurrentUnits,
        BarrelMaxUnits,
        300f
      );
      if (drained > 0)
      {
        BarrelMetalContent = content;
        BarrelCurrentUnits += drained;
        PlayDrainSound();
        MarkDirty(true);
      }
    }
    else if (IsMold && !IsContentHardened(MoldMetalContent))
    {
      var content = MoldMetalContent;
      int drained = DrainInto(
        ref content,
        MoldCurrentUnits,
        MoldMaxUnits,
        SmexValues.MoltenCooldownSpeed
      );
      if (drained > 0)
      {
        MoldMetalContent = content;
        MoldCurrentUnits += drained;
        PlayDrainSound();
        MarkDirty(true);
      }
    }
  }

  private void PlayDrainSound() =>
    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.MoltenMetal,
      ref _lastDrainSoundMs,
      2000,
      0.5f
    );

  // Returns the units drained from this cell into <paramref name="content"/>,
  // creating/heating the content stack as needed. 0 if nothing was drained.
  private int DrainInto(
    ref ItemStack? content,
    int currentUnits,
    int maxUnits,
    float cooldownSpeed
  )
  {
    if (currentUnits >= maxUnits)
      return 0;
    if (
      content != null
      && CellMetalType.Length > 0
      && content.Collectible.Code.ToString() != CellMetalType
    )
      return 0;

    int space = maxUnits - currentUnits;
    int toDrain = (int)Math.Min(_drainSpeed, space);
    if (toDrain <= 0)
      return 0;

    // Capture metal identity/temperature before draining empties the cell.
    string type = CellMetalType;
    float temp = CellTemperature;

    float drained = DrainMetal(toDrain);
    if (drained <= 0f)
      return 0;

    if (content == null)
    {
      content = MoltenMetal.CreateStack(Api.World, type, temp, cooldownSpeed);
      if (content == null)
        return 0;
    }
    else
    {
      MoltenMetal.SetTemperature(Api.World, content, temp);
    }
    return (int)drained;
  }

  private bool IsContentHardened(ItemStack? content) =>
    content != null && MoltenMetal.IsHardened(Api.World, content);

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("isPouring", IsPouring);

    tree.SetBool("isBarrel", IsBarrel);
    tree.SetItemstack("barrelContents", BarrelMetalContent);
    tree.SetInt("barrelCurrentUnits", BarrelCurrentUnits);
    tree.SetInt("barrelMaxUnits", BarrelMaxUnits);

    tree.SetBool("isMold", IsMold);
    tree.SetItemstack("moldStack", MoldStack);
    tree.SetItemstack("moldContents", MoldMetalContent);
    tree.SetInt("moldCurrentUnits", MoldCurrentUnits);
    tree.SetInt("moldMaxUnits", MoldMaxUnits);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    bool wasBarrel = IsBarrel;
    bool wasMold = IsMold;
    base.FromTreeAttributes(tree, worldForResolving);
    IsPouring = tree.GetBool("isPouring", true);

    IsBarrel = tree.GetBool("isBarrel");
    BarrelMetalContent = tree.GetItemstack("barrelContents");
    BarrelMetalContent?.ResolveBlockOrItem(worldForResolving);
    BarrelCurrentUnits = tree.GetInt("barrelCurrentUnits");
    BarrelMaxUnits = tree.GetInt(
      "barrelMaxUnits",
      SmexValues.BarrelDefaultMaxUnits
    );

    IsMold = tree.GetBool("isMold");
    MoldStack = tree.GetItemstack("moldStack");
    MoldStack?.ResolveBlockOrItem(worldForResolving);
    MoldMetalContent = tree.GetItemstack("moldContents");
    MoldMetalContent?.ResolveBlockOrItem(worldForResolving);
    MoldCurrentUnits = tree.GetInt("moldCurrentUnits");
    MoldMaxUnits = tree.GetInt("moldMaxUnits", SmexValues.MoldDefaultUnits);

    if (IsBarrel != wasBarrel || IsMold != wasMold)
    {
      _moldMesh = null;
      _tessellatedMoldCode = null;
      MarkDirty(true);
    }
    if (Api?.Side == EnumAppSide.Client)
      UpdateRenderer();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    if (IsBarrel)
      AppendContentInfo(
        dsc,
        Lang.Get("smex:canal-label-barrel"),
        BarrelMetalContent,
        BarrelCurrentUnits,
        BarrelMaxUnits
      );
    else if (IsMold)
      AppendContentInfo(
        dsc,
        Lang.Get("smex:canal-label-mold"),
        MoldMetalContent,
        MoldCurrentUnits,
        MoldMaxUnits
      );
    else
      dsc.AppendLine(Lang.Get("smex:canal-info-empty"));

    dsc.AppendLine(
      Lang.Get(
        "smex:canal-pouring",
        Lang.Get(IsPouring ? "smex:state-on" : "smex:state-off")
      )
    );
  }

  private void AppendContentInfo(
    StringBuilder dsc,
    string label,
    ItemStack? content,
    int currentUnits,
    int maxUnits
  )
  {
    if (content == null || currentUnits <= 0)
    {
      dsc.AppendLine(Lang.Get("smex:canal-content-empty", label));
      return;
    }

    float temp = MoltenMetal.GetTemperature(Api.World, content);
    string state = Lang.Get(
      MoltenMetal.StateOf(Api.World, content) switch
      {
        MoltenState.Liquid => "smex:metalstate-liquid",
        MoltenState.Hardened => "smex:metalstate-hardened",
        _ => "smex:metalstate-cooling",
      }
    );
    dsc.AppendLine(
      Lang.Get(
        "smex:canal-content",
        label,
        currentUnits,
        maxUnits,
        MoltenMetal.DisplayName(content.Collectible.Code.ToString()),
        state,
        MoltenMetal.FormatTemperature(temp)
      )
    );
  }

  #endregion

  #region Tessellation

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  )
  {
    base.OnTesselation(mesher, tesselator);

    if (IsBarrel)
    {
      if (_barrelMesh == null)
      {
        Shape? barrelShape = Api.Assets.Get<Shape>(
          new AssetLocation("smex:shapes/molten/barrel.json")
        );
        if (barrelShape != null)
          tesselator.TesselateShape(Block, barrelShape, out _barrelMesh);
      }
      if (_barrelMesh != null)
        mesher.AddMeshData(_barrelMesh);
    }
    else if (IsMold && MoldStack?.Block != null)
    {
      if (
        _moldMesh == null
        || !Equals(_tessellatedMoldCode, MoldStack.Block.Code)
      )
      {
        tesselator.TesselateBlock(MoldStack.Block, out _moldMesh);
        _tessellatedMoldCode = MoldStack.Block.Code;
        _moldMesh.Translate(0f, MoldMeshRaiseY, 0f);

        float rotY = (Block?.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;
        if (rotY != 0f)
          _moldMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, rotY, 0f);
      }
      mesher.AddMeshData(_moldMesh);
    }

    if (!IsPouring)
    {
      if (_tapEndMesh == null && Orientation != null)
        _tapEndMesh = MoltenMeshes.TesselateEndCap(
          Api,
          tesselator,
          Block!,
          BlockFacing.FromFirstLetter(Orientation)
        );
      if (_tapEndMesh != null)
        mesher.AddMeshData(_tapEndMesh);
    }
    else
    {
      _tapEndMesh = null;
    }

    return true;
  }

  #endregion
}

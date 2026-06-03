using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Molten.BlockEntities;

/// <summary>
/// Block entity for the canal tap: pours the network's liquid metal into whatever
/// is parked beneath it — a molten barrel or a large tool mold (anvil, helve
/// hammer) — draining the network each tick while pouring is enabled.
/// </summary>
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
    float rotY = (block.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;

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
    var quadDefs = block.Attributes?[
      "fillQuadsByLevel"
    ]?.AsObject<FillQuadDef[]>();
    Cuboidf[] boxes = quadDefs is { Length: > 0 }
      ? [.. quadDefs.Select(q => new Cuboidf(q.x1, 0f, q.z1, q.x2, 16f, q.z2))]
      : [new Cuboidf(4f, 0f, 4f, 12f, 16f, 12f)];

    fillStartY = (block.Attributes?["fillStart"]?.AsFloat(1f) ?? 1f) / 16f;
    fillHeightLevels = block.Attributes?["fillHeight"]?.AsFloat(8f) ?? 8f;
    return boxes;
  }

  #endregion

  #region Barrel attach / detach

  /// <summary>Parks <paramref name="barrelStack"/> under the tap, adopting any metal it already holds.</summary>
  public void AddBarrel(ItemStack barrelStack)
  {
    if (
      barrelStack.Attributes["blockEntityAttributes"] is ITreeAttribute beData
    )
    {
      BarrelMetalContent = beData.GetItemstack("contents");
      BarrelMetalContent?.ResolveBlockOrItem(Api.World);
      BarrelCurrentUnits = beData.GetInt("currentUnitAmount");
    }
    else
    {
      BarrelMetalContent = null;
      BarrelCurrentUnits = 0;
    }
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
    if (BarrelMetalContent != null || BarrelCurrentUnits > 0)
    {
      var beData = new TreeAttribute();
      beData.SetItemstack("contents", BarrelMetalContent);
      beData.SetInt("currentUnitAmount", BarrelCurrentUnits);
      stack.Attributes["blockEntityAttributes"] = beData;
    }
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

    if (itemStack.Attributes?["blockEntityAttributes"] is ITreeAttribute beData)
    {
      MoldMetalContent = beData.GetItemstack("contents");
      MoldMetalContent?.ResolveBlockOrItem(Api.World);
      MoldCurrentUnits = beData.GetInt("fillLevel");
    }
    else
    {
      MoldMetalContent = null;
      MoldCurrentUnits = 0;
    }

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

    if (MoldMetalContent != null && MoldCurrentUnits > 0)
    {
      var beData = new TreeAttribute();
      beData.SetItemstack("contents", MoldMetalContent.Clone());
      beData.SetInt("fillLevel", MoldCurrentUnits);
      beData.SetBool("shattered", false);
      beData.SetFloat("meshAngle", 0f);
      stack.Attributes["blockEntityAttributes"] = beData;
    }

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
    SmexSounds.Play(Api, Pos, SmexSounds.Latch, 0.7f);
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
    SmexSounds.PlayThrottled(
      Api,
      Pos,
      SmexSounds.MoltenMetal,
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
      Item? collectible =
        type.Length > 0 ? Api.World.GetItem(new AssetLocation(type)) : null;
      if (collectible == null)
        return 0;
      content = new ItemStack(collectible, 1);
      (content.Attributes["temperature"] as ITreeAttribute)?.SetFloat(
        "cooldownSpeed",
        cooldownSpeed
      );
    }
    content.Collectible.SetTemperature(
      Api.World,
      content,
      temp,
      delayCooldown: false
    );
    return (int)drained;
  }

  private bool IsContentHardened(ItemStack? content)
  {
    if (content == null)
      return false;
    float temp = content.Collectible.GetTemperature(Api.World, content);
    float meltPoint = content.Collectible.GetMeltingPoint(
      Api.World,
      null,
      new DummySlot(content)
    );
    return temp < 0.3f * meltPoint;
  }

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

    float temp = content.Collectible.GetTemperature(Api.World, content);
    float meltPoint = content.Collectible.GetMeltingPoint(
      Api.World,
      null,
      new DummySlot(content)
    );
    string state = Lang.Get(
      temp > 0.8f * meltPoint ? "smex:metalstate-liquid"
      : temp < 0.3f * meltPoint ? "smex:metalstate-hardened"
      : "smex:metalstate-cooling"
    );
    string tempStr =
      temp < 21f ? Lang.Get("smex:metalstate-cold") : $"{temp:F0}°C";
    string path = content.Collectible.Code.Path;
    string metalName = path.StartsWith("ingot-") ? path[6..] : path;
    metalName =
      metalName.Length > 0
        ? char.ToUpper(metalName[0]) + metalName[1..]
        : metalName;
    dsc.AppendLine(
      Lang.Get(
        "smex:canal-content",
        label,
        currentUnits,
        maxUnits,
        metalName,
        state,
        tempStr
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
      {
        var endShape = Api.Assets.Get<Shape>(
          new AssetLocation("smex:shapes/molten/canal/end.json")
        );
        if (endShape != null)
        {
          tesselator.TesselateShape(Block, endShape, out _tapEndMesh);
          var face = BlockFacing.FromFirstLetter(Orientation);
          float rotY =
            face.Index switch
            {
              BlockFacing.indexNORTH => 180f,
              BlockFacing.indexEAST => 90f,
              BlockFacing.indexWEST => 270f,
              _ => 0f,
            } * GameMath.DEG2RAD;
          if (rotY != 0f)
            _tapEndMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, rotY, 0f);
        }
      }
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

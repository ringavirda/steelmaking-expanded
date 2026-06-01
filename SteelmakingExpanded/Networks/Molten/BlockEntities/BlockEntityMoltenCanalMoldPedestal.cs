using System;
using System.Linq;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Molten.BlockEntities;

/// <summary>
/// Block entity for the mold pedestal: a canal node that holds one small tool mold
/// and drains the network's liquid metal into it each tick until full or hardened.
/// </summary>
public class BlockEntityMoltenCanalMoldPedestal : BlockEntityMoltenCanal
{
  /// <summary>Fallback mold capacity in units when the mold defines no <c>requiredUnits</c>.</summary>
  public const int DefaultMoldUnits = 100;

  /// <summary>Whether a mold is currently placed on the pedestal.</summary>
  public bool IsMold { get; set; } = false;

  /// <summary>Whether the pedestal is actively filling the mold from the network.</summary>
  public bool IsPouring { get; private set; } = true;

  /// <summary>The placed mold item, or <c>null</c> when empty.</summary>
  public ItemStack? MoldStack { get; private set; }

  /// <summary>The metal cast into the mold, or <c>null</c>.</summary>
  public ItemStack? MoldMetalContent { get; private set; }

  /// <summary>Units of metal currently in the mold.</summary>
  public int MoldCurrentUnits { get; private set; }

  /// <summary>The placed mold's capacity in units.</summary>
  public int MoldMaxUnits { get; private set; } = DefaultMoldUnits;

  /// <summary>Toggles whether the pedestal fills its mold from the network.</summary>
  public void TryTogglePouring()
  {
    IsPouring = !IsPouring;
    SmexSounds.Play(Api, Pos, SmexSounds.Latch, 0.7f);
    MarkDirty(true);
  }

  private MeshData? _moldMesh;
  private AssetLocation? _tessellatedMoldCode;
  private MoltenRenderer? _moldRenderer;
  private MeshData? _endMesh;

  // Throttle for the looping molten-pour hiss while draining into the mold.
  private long _lastDrainSoundMs;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnServerTick, 1000);
  }

  public override void OnBlockRemoved()
  {
    _moldRenderer?.Dispose();
    _moldRenderer = null;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    _moldRenderer?.Dispose();
    _moldRenderer = null;
    base.OnBlockUnloaded();
  }

  #region Mold attach / detach

  /// <summary>Places <paramref name="itemStack"/> as the pedestal's mold, adopting any metal it already holds.</summary>
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
      MoldStack.Block?.Attributes?["requiredUnits"].AsInt(DefaultMoldUnits)
      ?? DefaultMoldUnits;
    IsMold = true;
  }

  /// <summary>Removes the mold and returns it, preserving any cast metal in its <c>blockEntityAttributes</c>.</summary>
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
      // vanilla writes meshAngle too; without it hasMeshAngle triggers the
      // server-side block-exchange path in OnLoadWithoutMeshAngle
      beData.SetFloat("meshAngle", 0f);
      stack.Attributes["blockEntityAttributes"] = beData;
    }

    MoldStack = null;
    MoldMetalContent = null;
    MoldCurrentUnits = 0;
    MoldMaxUnits = DefaultMoldUnits;
    return stack;
  }

  #endregion

  #region Server tick: drain network into mold

  private void OnServerTick(float dt)
  {
    if (
      !IsMold
      || !IsPouring
      || MoldCurrentUnits >= MoldMaxUnits
      || IsMoldHardened()
    )
      return;
    if (NetworkSystem?.GetNetworkAt(Pos) is not MoltenNetwork network)
      return;
    if (
      network.State is not MoltenNetworkState netState
      || netState.CurrentAmount <= 0f
      || netState.Solidified
    )
      return;

    if (
      MoldMetalContent != null
      && netState.MetalType.Length > 0
      && MoldMetalContent.Collectible.Code.ToString() != netState.MetalType
    )
      return;

    int space = MoldMaxUnits - MoldCurrentUnits;
    int toDrain = (int)Math.Min(netState.CurrentAmount, space);
    if (toDrain <= 0)
      return;

    float drained = network.DrainMetal(toDrain, Api.World.BlockAccessor);
    if (drained <= 0f)
      return;

    if (MoldMetalContent == null)
    {
      var collectible =
        netState.MetalStack?.Collectible
        ?? (
          netState.MetalType.Length > 0
            ? Api.World.GetItem(new AssetLocation(netState.MetalType))
            : null
        );
      if (collectible == null)
        return;
      MoldMetalContent = new ItemStack(collectible, 1);
      (MoldMetalContent.Attributes["temperature"] as ITreeAttribute)?.SetFloat(
        "cooldownSpeed",
        40f
      );
    }
    MoldMetalContent.Collectible.SetTemperature(
      Api.World,
      MoldMetalContent,
      netState.CurrentTemperature,
      delayCooldown: false
    );
    MoldCurrentUnits += (int)drained;
    SmexSounds.PlayThrottled(
      Api,
      Pos,
      SmexSounds.MoltenMetal,
      ref _lastDrainSoundMs,
      2000,
      0.5f
    );
    MarkDirty(true);
  }

  private bool IsMoldHardened()
  {
    if (MoldMetalContent == null || MoldCurrentUnits <= 0)
      return false;
    float temp = MoldMetalContent.Collectible.GetTemperature(
      Api.World,
      MoldMetalContent
    );
    float meltPoint = MoldMetalContent.Collectible.GetMeltingPoint(
      Api.World,
      null,
      new DummySlot(MoldMetalContent)
    );
    return temp < 0.3f * meltPoint;
  }

  #endregion

  #region Renderer

  protected override void InitRenderer(ICoreClientAPI capi)
  {
    base.InitRenderer(capi);

    var quadDefs = Block?.Attributes?[
      "moldFillQuadsByLevel"
    ]?.AsObject<FillQuadDef[]>();
    Cuboidf[] boxes = quadDefs is { Length: > 0 }
      ? [.. quadDefs.Select(q => new Cuboidf(q.x1, 0f, q.z1, q.x2, 16f, q.z2))]
      : [new Cuboidf(7f, 0f, 0f, 9f, 16f, 5f)];

    int fillStartPx = Block?.Attributes?["moldFillStart"]?.AsInt(14) ?? 14;
    int fillHeightLevels = Block?.Attributes?["moldFillHeight"]?.AsInt(1) ?? 1;
    float fillStartY = fillStartPx / 16f;
    float rotY = (float)((Block?.Shape?.rotateY ?? 0f) * Math.PI / 180.0);

    _moldRenderer = new MoltenRenderer(
      Pos,
      capi,
      boxes,
      rotY,
      fillStartY,
      fillHeightLevels
    );
    capi.Event.RegisterRenderer(_moldRenderer, EnumRenderStage.Opaque);
  }

  protected override void UpdateRenderer()
  {
    base.UpdateRenderer();

    if (_moldRenderer == null)
      return;

    if (!IsMold || MoldMetalContent == null || MoldCurrentUnits <= 0)
    {
      _moldRenderer.FillRatio = 0f;
      _moldRenderer.MetalStack = null;
      return;
    }

    _moldRenderer.FillRatio = MoldCurrentUnits / (float)MoldMaxUnits;
    _moldRenderer.Temperature = MoldMetalContent.Collectible.GetTemperature(
      Api.World,
      MoldMetalContent
    );
    _moldRenderer.MetalStack = MoldMetalContent;
  }

  #endregion

  #region Tessellation

  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  )
  {
    base.OnTesselation(mesher, tesselator);

    // When pouring is disabled, cap the inlet with the canal end piece — same as
    // the canal tap — so a paused pedestal visually reads as closed.
    if (!IsPouring)
    {
      if (_endMesh == null && Orientation != null)
      {
        var endShape = Api.Assets.Get<Shape>(
          new AssetLocation("smex:shapes/molten/canal/end.json")
        );
        if (endShape != null)
        {
          tesselator.TesselateShape(Block, endShape, out _endMesh);
          var face = BlockFacing.FromFirstLetter(Orientation);
          float endRotY =
            face.Index switch
            {
              BlockFacing.indexNORTH => 180f,
              BlockFacing.indexEAST => 90f,
              BlockFacing.indexWEST => 270f,
              _ => 0f,
            } * GameMath.DEG2RAD;
          if (endRotY != 0f)
            _endMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, endRotY, 0f);
        }
      }
      if (_endMesh != null)
        mesher.AddMeshData(_endMesh);
    }
    else
    {
      _endMesh = null;
    }

    if (!IsMold || MoldStack?.Block == null)
      return true;

    if (
      _moldMesh == null
      || !Equals(_tessellatedMoldCode, MoldStack.Block.Code)
    )
    {
      tesselator.TesselateBlock(MoldStack.Block, out _moldMesh);
      _tessellatedMoldCode = MoldStack.Block.Code;

      // Pedestal main body tops out at y = 11/16; translate the mold mesh up
      // so it rests on the stone surface rather than floating at block origin.
      _moldMesh.Translate(0f, 11f / 16f, 0f);

      float rotY = (Block?.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;
      if (rotY != 0f)
        _moldMesh.Rotate(new Vec3f(0.5f, 0.5f, 0.5f), 0f, rotY, 0f);
    }
    mesher.AddMeshData(_moldMesh);
    return true;
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("isMold", IsMold);
    tree.SetBool("isPouring", IsPouring);
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
    base.FromTreeAttributes(tree, worldForResolving);

    IsMold = tree.GetBool("isMold");
    IsPouring = tree.GetBool("isPouring", true);
    MoldStack = tree.GetItemstack("moldStack");
    MoldStack?.ResolveBlockOrItem(worldForResolving);
    MoldMetalContent = tree.GetItemstack("moldContents");
    MoldMetalContent?.ResolveBlockOrItem(worldForResolving);
    MoldCurrentUnits = tree.GetInt("moldCurrentUnits");
    MoldMaxUnits = tree.GetInt("moldMaxUnits", DefaultMoldUnits);
    UpdateRenderer();
  }

  #endregion

  #region Block info

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    dsc.AppendLine(
      Lang.Get(
        "smex:canal-pouring",
        Lang.Get(IsPouring ? "smex:state-on" : "smex:state-off")
      )
    );

    if (!IsMold)
    {
      dsc.AppendLine(Lang.Get("smex:moldpedestal-nomold"));
      return;
    }

    if (MoldMetalContent == null || MoldCurrentUnits <= 0)
    {
      dsc.AppendLine(Lang.Get("smex:mold-empty"));
      return;
    }

    float temp = MoldMetalContent.Collectible.GetTemperature(
      Api.World,
      MoldMetalContent
    );
    float meltPoint = MoldMetalContent.Collectible.GetMeltingPoint(
      Api.World,
      null,
      new DummySlot(MoldMetalContent)
    );
    string state = Lang.Get(
      temp > 0.8f * meltPoint ? "smex:metalstate-liquid"
      : temp < 0.3f * meltPoint ? "smex:metalstate-hardened"
      : "smex:metalstate-cooling"
    );
    string tempStr =
      temp < 21f ? Lang.Get("smex:metalstate-cold") : $"{temp:F0}°C";
    string path = MoldMetalContent.Collectible.Code.Path;
    string metalName = path.StartsWith("ingot-") ? path[6..] : path;
    metalName =
      metalName.Length > 0
        ? char.ToUpper(metalName[0]) + metalName[1..]
        : metalName;
    dsc.AppendLine(
      Lang.Get(
        "smex:mold-content",
        MoldCurrentUnits,
        MoldMaxUnits,
        metalName,
        state,
        tempStr
      )
    );
  }

  #endregion
}

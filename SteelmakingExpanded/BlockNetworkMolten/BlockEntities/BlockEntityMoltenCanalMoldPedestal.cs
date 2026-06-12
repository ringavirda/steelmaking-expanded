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
/// Block entity for the mold pedestal: a canal node that holds one small tool mold
/// and drains the network's liquid metal into it each tick until full or hardened.
/// </summary>
[EntityRegister]
public class BlockEntityMoltenCanalMoldPedestal : BlockEntityMoltenCanal
{
  /// <summary>Whether a mold is currently placed on the pedestal.</summary>
  public bool IsMold { get; set; } = false;

  private bool _isPouring = true;

  /// <summary>Whether the pedestal is actively filling the mold from the network.</summary>
  public bool IsPouring
  {
    get => _isPouring;
    private set
    {
      if (_isPouring == value)
        return;
      _isPouring = value;
      // Open/closed flips IsConnectionBroken, so re-walk the graph to sever the
      // pedestal from (or rejoin it to) the run. No-op off-server / before
      // Initialize, where base.Initialize's AddNode registers the right state.
      ResyncNetworkNode();
    }
  }

  // The pedestal is a drain fitting — its cell must keep delivering to the mold,
  // so it never clogs like a plain canal run.
  protected override bool SolidifiesWhenCold => false;

  // A closed pedestal severs itself from the run (it's a single-connector leaf) so
  // no metal flows into its cell — IsPouring otherwise only gates its own draining
  // into the mold, leaving the cell to keep filling from the network.
  public override bool IsConnectionBroken() =>
    base.IsConnectionBroken() || !IsPouring;

  /// <summary>The placed mold item, or <c>null</c> when empty.</summary>
  public ItemStack? MoldStack { get; private set; }

  /// <summary>The metal cast into the mold, or <c>null</c>.</summary>
  public ItemStack? MoldMetalContent { get; private set; }

  /// <summary>Units of metal currently in the mold.</summary>
  public int MoldCurrentUnits { get; private set; }

  /// <summary>The placed mold's capacity in units.</summary>
  public int MoldMaxUnits { get; private set; } = SmexValues.MoldDefaultUnits;

  /// <summary> Mold pedestal by itself has low capacity. </summary>
  public override int MaxUnitCapacity =>
    (int)Math.Ceiling(SmexValues.CanalDefaultUnitCapacity / 2.0);

  /// <summary>Toggles whether the pedestal fills its mold from the network.</summary>
  public void TryTogglePouring()
  {
    IsPouring = !IsPouring;
    ExSounds.Play(Api, Pos, ExSounds.Latch, 0.7f);
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
    else
      // The mold metal keeps cooling after the pour stops broadcasting (full /
      // hardened), so refresh the surface glow on the client from the stack's live
      // temperature — otherwise it freezes at the last server value and snaps cold
      // on the next interaction.
      RegisterGameTickListener(_ => UpdateRenderer(), 1000);
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
  }

  /// <summary>Removes the mold and returns it, preserving any cast metal in its <c>blockEntityAttributes</c>.</summary>
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
    return stack;
  }

  #endregion

  #region Server tick: drain network into mold

  private void OnServerTick(float dt)
  {
    // The pedestal drains its own cell (where the run delivers metal) into the mold.
    if (
      !IsMold
      || !IsPouring
      || MoldCurrentUnits >= MoldMaxUnits
      || IsMoldHardened()
      || !HasMoltenMetal
    )
      return;

    if (
      MoldMetalContent != null
      && CellMetalType.Length > 0
      && MoldMetalContent.Collectible.Code.ToString() != CellMetalType
    )
      return;

    int space = MoldMaxUnits - MoldCurrentUnits;
    int toDrain = Math.Min(CellAmount, space);
    if (toDrain <= 0)
      return;

    // Capture metal identity/temperature before draining empties the cell.
    string type = CellMetalType;
    float temp = CellTemperature;

    float drained = DrainMetal(toDrain);
    if (drained <= 0f)
      return;

    if (MoldMetalContent == null)
    {
      MoldMetalContent = MoltenMetal.CreateStack(Api.World, type, temp);
      if (MoldMetalContent == null)
        return;
    }
    else
    {
      MoltenMetal.SetTemperature(Api.World, MoldMetalContent, temp);
    }
    MoldCurrentUnits += (int)drained;
    ExSounds.PlayThrottled(
      Api,
      Pos,
      ExSounds.MoltenMetal,
      ref _lastDrainSoundMs,
      2000,
      0.5f
    );
    MarkDirty(true);
  }

  private bool IsMoldHardened() =>
    MoldMetalContent != null
    && MoldCurrentUnits > 0
    && MoltenMetal.IsHardened(Api.World, MoldMetalContent);

  #endregion

  #region Renderer

  protected override void InitRenderer(ICoreClientAPI capi)
  {
    base.InitRenderer(capi);

    Cuboidf[] boxes = FillQuads.ReadBoxes(
      Block,
      "moldFillQuadsByLevel",
      new Cuboidf(7f, 0f, 0f, 9f, 16f, 5f)
    );
    float fillStartY = FillQuads.ReadStartY(Block, "moldFillStart", 14f);
    float fillHeightLevels = FillQuads.ReadHeightLevels(
      Block,
      "moldFillHeight",
      1f
    );
    float rotY = (Block?.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;

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
        _endMesh = MoltenMeshes.TesselateEndCap(
          Api,
          tesselator,
          Block!,
          BlockFacing.FromFirstLetter(Orientation)
        );
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
    MoldMaxUnits = tree.GetInt("moldMaxUnits", SmexValues.MoldDefaultUnits);
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

    string state = Lang.Get(
      MoltenMetal.StateOf(Api.World, MoldMetalContent) switch
      {
        MoltenState.Liquid => "smex:metalstate-liquid",
        MoltenState.Hardened => "smex:metalstate-hardened",
        _ => "smex:metalstate-cooling",
      }
    );
    dsc.AppendLine(
      Lang.Get(
        "smex:mold-content",
        MoldCurrentUnits,
        MoldMaxUnits,
        MoltenMetal.DisplayName(MoldMetalContent.Collectible.Code.ToString()),
        state,
        MoltenMetal.FormatTemperature(
          MoltenMetal.GetTemperature(Api.World, MoldMetalContent)
        )
      )
    );
  }

  #endregion
}

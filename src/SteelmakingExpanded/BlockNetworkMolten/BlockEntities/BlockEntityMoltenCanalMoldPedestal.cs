using System;
using System.Linq;
using System.Text;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using SteelmakingExpanded.Molds;
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
[BlockEntityRegister]
public class BlockEntityMoltenCanalMoldPedestal : BlockEntityMoltenCanal {
  /// <summary>Whether a mold is currently placed on the pedestal.</summary>
  public bool IsMold { get; set; } = false;

  private bool _isPouring = true;

  /// <summary>Whether the pedestal is actively filling the mold from the network.</summary>
  public bool IsPouring {
    get => _isPouring;
    private set {
      if (_isPouring == value)
        return;
      _isPouring = value;
      // Open/closed flips IsConnectionBroken, so re-walk the graph to sever/rejoin the pedestal.
      // No-op off-server / before Initialize (where base.Initialize's AddNode handles it).
      ResyncNetworkNode();
    }
  }

  // The pedestal's own cell clogs and is chiselled clear like any canal or the start block: it
  // inherits the default SolidifiesWhenCold (true). Metal being actively delivered stays hot, so it
  // only freezes once the run goes cold with metal left standing in the cell - then HasMoltenMetal/
  // IsConnectionBroken stop the fill and sever it until the player chips it out.

  // A closed pedestal severs itself from the run (single-connector leaf) so no metal flows into
  // its cell - otherwise IsPouring only gates draining into the mold, leaving the cell to fill.
  // Solidified (base) severs it too, so a frozen cell drops off the run until cleared.
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

  // Cooldown rate for metal cast in the mold: the molten-system base scaled by the pedestal's mold
  // coefficient (mirrors the converter's charge cooldown). Read live so a config change applies now.
  private static float MoldCooldownSpeed =>
    SmexValues.MoltenCooldownSpeed * SmexValues.MoldPedestalCooldownCoefficient;

  /// <summary>Toggles whether the pedestal fills its mold from the network.</summary>
  public void TryTogglePouring() {
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

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnServerTick, 1000);
    else
      // Mold metal keeps cooling after the pour stops broadcasting, so refresh the surface glow
      // from the stack's live temperature, or it freezes hot and snaps cold on interaction.
      RegisterGameTickListener(_ => UpdateRenderer(), 1000);
  }

  public override void OnBlockRemoved() {
    _moldRenderer?.Dispose();
    _moldRenderer = null;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    _moldRenderer?.Dispose();
    _moldRenderer = null;
    base.OnBlockUnloaded();
  }

  #region Mold attach / detach

  /// <summary>Places <paramref name="itemStack"/> as the pedestal's mold, adopting any metal it already holds.</summary>
  public void AddMold(ItemStack itemStack) {
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
  public ItemStack RemoveMold() {
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

  private void OnServerTick(float dt) {
    // A mold whose type a server admin disabled (/exmod molds ... off) is purged from the pedestal
    // on load - the world-block/inventory copies are removed by the shared migration sweep, but a
    // pedestal's mold is stored outside any inventory, so clear it here.
    if (IsMold && MoldGating.IsToolMoldDisabled(MoldStack?.Collectible?.Code)) {
      RemoveMold();
      MarkDirty(true);
      return;
    }

    // Keep the cast mold's cooldown rate in step with the live config (before the pour gate) so a
    // `/exmod config smex MoltenCooldownSpeed ...` change affects metal already cast in the mold.
    if (IsMold && MoldMetalContent != null && MoldCurrentUnits > 0)
      MoltenMetal.SyncCooldownSpeed(
        Api.World,
        MoldMetalContent,
        MoldCooldownSpeed
      );

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

    if (MoldMetalContent == null) {
      MoldMetalContent = MoltenMetal.CreateStack(
        Api.World,
        type,
        temp,
        MoldCooldownSpeed
      );
      if (MoldMetalContent == null)
        return;
    } else {
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

  protected override void InitRenderer(ICoreClientAPI capi) {
    base.InitRenderer(capi);

    if (Block is not BlockMoltenCanalMoldPedestal pedestal)
      return;

    Cuboidf[] boxes = FillQuads.BoxesFrom(
      pedestal.MoldFillQuadsByLevel,
      new Cuboidf(7f, 0f, 0f, 9f, 16f, 5f)
    );
    float fillStartY = BlockMoltenCanalMoldPedestal.MoldFillStart / 16f;
    float fillHeightLevels = BlockMoltenCanalMoldPedestal.MoldFillHeight;
    float rotY = (pedestal.Shape?.rotateY ?? 0f) * GameMath.DEG2RAD;

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

  protected override void UpdateRenderer() {
    base.UpdateRenderer();

    if (_moldRenderer == null)
      return;

    if (!IsMold || MoldMetalContent == null || MoldCurrentUnits <= 0) {
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
  ) {
    base.OnTesselation(mesher, tesselator);

    // Pouring disabled: cap the inlet with the canal end piece so it reads as closed (like the tap).
    if (!IsPouring) {
      if (_endMesh == null && Orientation != null)
        _endMesh = MoltenMeshes.TesselateEndCap(
          Api,
          tesselator,
          Block!,
          BlockFacing.FromFirstLetter(Orientation)
        );
      if (_endMesh != null)
        mesher.AddMeshData(_endMesh);
    } else {
      _endMesh = null;
    }

    if (!IsMold || MoldStack?.Block == null)
      return true;

    if (
      _moldMesh == null
      || !Equals(_tessellatedMoldCode, MoldStack.Block.Code)
    ) {
      tesselator.TesselateBlock(MoldStack.Block, out _moldMesh);
      _tessellatedMoldCode = MoldStack.Block.Code;

      // Body tops out at y = 11/16; raise the mold mesh so it rests on the surface.
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

  public override void ToTreeAttributes(ITreeAttribute tree) {
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
  ) {
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

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);

    dsc.AppendLine(
      Lang.Get(
        "smex:canal-pouring",
        Lang.Get(IsPouring ? "smex:state-on" : "smex:state-off")
      )
    );

    if (!IsMold) {
      dsc.AppendLine(Lang.Get("smex:moldpedestal-nomold"));
      return;
    }

    if (MoldMetalContent == null || MoldCurrentUnits <= 0) {
      dsc.AppendLine(Lang.Get("smex:mold-empty"));
      return;
    }

    string state = Lang.Get(
      MoltenMetal.StateOf(Api.World, MoldMetalContent) switch {
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

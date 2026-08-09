using System;
using System.Text;
using ExpandedLib.Blocks.Construction;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockNetworkMolten;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.Converter.BlockEntities;

/// <summary>
/// The big 3×3×3 converter shell. Construction is handled by the vanilla
/// <c>RightClickConstructable</c> behavior (which suppresses the default mesh, so the vessel
/// renders through the animator: a permanent <c>idle</c> animation re-tessellated to the built
/// elements, with <c>filling</c>/<c>pouring</c> as held tilt poses). It mirrors the operational
/// pose/charge from its <see cref="BlockEntityConverterControl"/> and hands solidified drops back
/// on break.
/// </summary>
[BlockEntityRegister]
public class BlockEntityConverterBessemer : BlockEntity, IChiselableMolten {
  private BlockPos? _controlPos;
  private ConverterOpState _opState = ConverterOpState.Normal;
  private bool _solidified;
  private int _chargeUnits;

  private BEBehaviorAnimatable? _animatable;
  private ExRightClickConstructable? _rcc;
  private bool _animatorReady;

  /// <summary>True once the player has finished the construction stages.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Whether the mirrored charge has solidified inside the vessel.</summary>
  public bool IsSolidified => _solidified;

  #region Lifecycle

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();
    _rcc = GetBehavior<ExRightClickConstructable>();

    if (api is ICoreClientAPI && _animatable != null) {
      // Re-render whenever the construction stage adds/removes elements.
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;

      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      ApplyPose();
    }
  }

  private string AnimCacheKey => "converterbessemer-" + Block.Variant["side"];

  public override void OnBlockRemoved() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    base.OnBlockUnloaded();
  }

  private void OnConstructShapeChanged(CompositeShape cs) {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyPose();
  }

  /// <summary>
  /// (Re)builds the animator to render exactly the currently-built elements (only the mesh is
  /// filtered to <paramref name="selectiveElements"/>; the animator hierarchy stays the full shape).
  /// </summary>
  private void RebuildAnimator(string[]? selectiveElements) {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    // CreateMesh resolves a FRESH shape each call; reusing one re-maps UVs into atlas space and
    // stretches textures. Rotation is applied by the renderer, not baked into the mesh.
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
    // A failed shape resolve leaves animUtil.animator null; only mark ready when it exists, so
    // ApplyPose never poses a null animator (vanilla GetBlockInfo would NRE). Same guard as boiler/engine.
    _animatorReady = _animatable.animUtil.animator != null;
  }

  #endregion

  #region Control link

  /// <summary>Records the position of the control block that drives this vessel.</summary>
  public void LinkControl(BlockPos controlPos) {
    _controlPos = controlPos.Copy();
    MarkDirty(true);
  }

  private BlockEntityConverterControl? GetControl() =>
    _controlPos == null
      ? null
      : Api.World.BlockAccessor.GetBlockEntity(_controlPos)
        as BlockEntityConverterControl;

  /// <summary>Server-side mirror update pushed by the control.</summary>
  public void UpdateMirror(
    bool solidified,
    int chargeUnits,
    ConverterOpState state
  ) {
    bool changed =
      _solidified != solidified
      || _chargeUnits != chargeUnits
      || _opState != state;
    _solidified = solidified;
    _chargeUnits = chargeUnits;
    _opState = state;
    if (changed)
      MarkDirty(true);
  }

  #endregion

  #region Animation

  // Spawn box for the rising smoke (block-relative): the opening in the InputLining element,
  // rotated per orientation by RotateXZ. Only spawns while upright, so no tilt math.
  private const float LiningX1 = -0.375f,
    LiningX2 = 0f;
  private const float LiningZ1 = 0.3125f,
    LiningZ2 = 0.6875f;
  private const float LiningY1 = 1.5f,
    LiningY2 = 2.0f;

  /// <summary>Emits rising smoke from the vessel mouth while refining; called from the control's tick.</summary>
  public void SpawnSmokeParticles(float intensity = 1f) {
    // Called from the control's server tick; server-spawned particles replicate to clients, so
    // don't gate on the client API here.
    if (Api == null)
      return;

    float x1 = LiningX1,
      z1 = LiningZ1;
    float x2 = LiningX2,
      z2 = LiningZ2;
    RotateXZ(ref x1, ref z1);
    RotateXZ(ref x2, ref z2);

    Vec3d minPos = new(
      Pos.X + Math.Min(x1, x2),
      Pos.Y + LiningY1,
      Pos.Z + Math.Min(z1, z2)
    );
    Vec3d maxPos = new(
      Pos.X + Math.Max(x1, x2),
      Pos.Y + LiningY2,
      Pos.Z + Math.Max(z1, z2)
    );

    ExParticles.RisingPlume(
      Api.World,
      ExParticles.Smoke,
      minPos,
      maxPos,
      // Lower upward velocity + shorter life ⇒ the plume only rises a short way
      // out of the vessel mouth instead of shooting toward the ceiling.
      new Vec3f(-0.15f, 0.5f, -0.15f),
      new Vec3f(0.15f, 1.1f, 0.15f),
      intensity * 6f,
      intensity * 10f,
      0.7f,
      -0.05f,
      0.15f,
      0.45f,
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, -120f),
      new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f)
    );
  }

  // Rotates a block-relative (x,z) around the block centre to match the shape's rotateY.
  private void RotateXZ(ref float x, ref float z) =>
    ExOrientation.RotateAroundCenter(
      ref x,
      ref z,
      ExOrientation.AngleFromSide(Block.Variant["side"])
    );

  private void ApplyPose() {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    util.StopAnimation("idle");
    util.StopAnimation("filling");
    util.StopAnimation("pouring");

    // Pose tilts only apply once the vessel is built; during construction it
    // simply renders the partial mesh at rest via "idle".
    string code = (IsConstructed ? _opState : ConverterOpState.Normal) switch {
      ConverterOpState.Filling => "filling",
      ConverterOpState.Pouring => "pouring",
      _ => "idle",
    };

    util.StartAnimation(
      new AnimationMetaData {
        Animation = code,
        Code = code,
        // The whole vessel tilts - slow and heavy. Idle just holds it visible.
        AnimationSpeed = code == "idle" ? 1f : 0.3f,
        EaseInSpeed = 3f,
        EaseOutSpeed = 3f,
      }.Init()
    );
  }

  #endregion

  #region Break handoff

  /// <summary>
  /// Returns the solidified drops to scatter (from the control's charge) and
  /// clears the control's charge. Returns null when nothing solidified.
  /// </summary>
  public ItemStack? CollectBreakDrops() => GetControl()?.OnConverterBroken();

  #endregion

  #region Chisel-out (IChiselableMolten, forwarded to the control)

  /// <summary>True when the vessel holds a solidified charge (see the control).</summary>
  public bool HasSolidifiedCharge => GetControl()?.HasSolidifiedCharge ?? false;

  /// <summary>True when the solidified charge has cooled to the chisellable (hardened) threshold.</summary>
  public bool ChargeIsHardened => GetControl()?.ChargeIsHardened ?? false;

  /// <summary>True when a small hardened residue can be chiselled out instead of breaking the vessel.</summary>
  public bool CanChiselOut() => GetControl()?.CanChiselOut() ?? false;

  /// <summary>Server-side: chips the hardened residue out via the control; null when not chiselable.</summary>
  public ItemStack? ChiselOutContent() => GetControl()?.ChiselOutContent();

  // IChiselableMolten: a solidified charge is the chiselable target; unlike a canal it also caps by size,
  // so the blocked feedback distinguishes "too full" (hardened but too big - break it) from "too hot".
  bool IChiselableMolten.HasChiselableContent => HasSolidifiedCharge;
  bool IChiselableMolten.CanChiselOut => CanChiselOut();
  string? IChiselableMolten.ChiselBlockedError =>
    ChargeIsHardened ? "smex-bessemertoofull" : "smex-bessemertoohot";

  ItemStack? IChiselableMolten.ChiselOut() => ChiselOutContent();

  #endregion

  #region HUD

  /// <summary>
  /// The vessel carries the live operational readout (charge, progress, power, status); the state
  /// lives on the control brain, which builds the text.
  /// </summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);

    // During construction the RCC interaction help covers what's next - no operational state yet.
    if (!IsConstructed)
      return;

    GetControl()?.AppendStructureState(forPlayer, dsc);
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    if (_controlPos != null) {
      tree.SetInt("ctrlX", _controlPos.X);
      tree.SetInt("ctrlY", _controlPos.Y);
      tree.SetInt("ctrlZ", _controlPos.Z);
    }
    tree.SetInt("opState", (int)_opState);
    tree.SetBool("solidified", _solidified);
    tree.SetInt("chargeUnits", _chargeUnits);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    if (tree.HasAttribute("ctrlX"))
      _controlPos = new BlockPos(
        tree.GetInt("ctrlX"),
        tree.GetInt("ctrlY"),
        tree.GetInt("ctrlZ")
      );

    var prevState = _opState;
    _opState = (ConverterOpState)tree.GetInt("opState");
    _solidified = tree.GetBool("solidified");
    _chargeUnits = tree.GetInt("chargeUnits");

    if (Api?.Side == EnumAppSide.Client && prevState != _opState)
      ApplyPose();
  }

  #endregion
}

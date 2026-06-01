using System;
using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Structures.BessemerConverter.BlockEntities;

/// <summary>
/// The big 3×3×3 converter shell. Construction is handled by the vanilla
/// <c>RightClickConstructable</c> behavior; this entity mirrors the operational
/// pose/charge from its <see cref="BlockEntityBessemerControl"/> and hands
/// solidified-charge drops back to the control on break.
/// <para>
/// RightClickConstructable suppresses the default block mesh, so we render the
/// vessel through the animator instead: a permanently-running <c>idle</c>
/// animation keeps it visible, re-tessellated to the currently-built elements
/// whenever the construction stage changes, with <c>filling</c>/<c>pouring</c>
/// layered on top as held tilt poses.
/// </para>
/// </summary>
public class BlockEntityBessemerConverter : BlockEntity
{
  private BlockPos? _controlPos;
  private BessemerOpState _opState = BessemerOpState.Normal;
  private bool _solidified;
  private int _chargeUnits;

  private BEBehaviorAnimatable? _animatable;
  private BEBehaviorRightClickConstructable? _rcc;
  private bool _animatorReady;

  /// <summary>True once the player has finished the construction stages.</summary>
  public bool IsConstructed => _rcc?.IsComplete ?? false;

  /// <summary>Whether the mirrored charge has solidified inside the vessel.</summary>
  public bool IsSolidified => _solidified;

  #region Lifecycle

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();
    _rcc = GetBehavior<BEBehaviorRightClickConstructable>();

    if (api is ICoreClientAPI && _animatable != null)
    {
      // Re-render whenever the construction stage adds/removes elements.
      if (_rcc != null)
        _rcc.OnShapeChanged += OnConstructShapeChanged;

      RebuildAnimator(_rcc?.shape?.SelectiveElements);
      ApplyPose();
    }
  }

  private string AnimCacheKey => "bessemerconverter-" + Block.Variant["side"];

  public override void OnBlockRemoved()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    if (_rcc != null)
      _rcc.OnShapeChanged -= OnConstructShapeChanged;
    base.OnBlockUnloaded();
  }

  private void OnConstructShapeChanged(CompositeShape cs)
  {
    RebuildAnimator(cs?.SelectiveElements);
    ApplyPose();
  }

  /// <summary>
  /// (Re)builds the animator so it renders exactly the currently-built
  /// elements. The animator hierarchy stays the full shape (stable cache key);
  /// only the rendered mesh is filtered to <paramref name="selectiveElements"/>.
  /// </summary>
  private void RebuildAnimator(string[]? selectiveElements)
  {
    if (Api is not ICoreClientAPI || _animatable == null)
      return;

    // CreateMesh loads and resolves a FRESH shape each call. Reusing a single
    // shape would re-map its UVs into atlas space on every construction stage,
    // compounding into stretched textures. Rotation is applied by the renderer
    // (passed to InitializeAnimator), not baked into the mesh.
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
    _animatorReady = true;
  }

  #endregion

  #region Control link

  /// <summary>Records the position of the control block that drives this vessel.</summary>
  public void LinkControl(BlockPos controlPos)
  {
    _controlPos = controlPos.Copy();
    MarkDirty(true);
  }

  private BlockEntityBessemerControl? GetControl() =>
    _controlPos == null
      ? null
      : Api.World.BlockAccessor.GetBlockEntity(_controlPos)
        as BlockEntityBessemerControl;

  /// <summary>Server-side mirror update pushed by the control.</summary>
  public void UpdateMirror(
    bool solidified,
    int chargeUnits,
    BessemerOpState state
  )
  {
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

  // Spawn box for the rising smoke, in block-relative units: the opening in the
  // InputLining shape element (the gap between cubes Cube226/Cube228, topping out
  // at y=32/16). Rotated per orientation by RotateXZ below. No tilt math is
  // needed — these only spawn while the vessel is upright (Normal/processing).
  private const float LiningX1 = -0.375f,
    LiningX2 = 0f;
  private const float LiningZ1 = 0.3125f,
    LiningZ2 = 0.6875f;
  private const float LiningY1 = 1.5f,
    LiningY2 = 2.0f;

  /// <summary>Emits rising smoke from the vessel mouth while refining; called from the control's tick.</summary>
  public void SpawnSmokeParticles(float intensity = 1f)
  {
    // Called from the control's server-side production tick. SimpleParticleProperties
    // spawned on the server are replicated to nearby clients, so we must NOT gate on
    // the client API here (the old `Api is not ICoreClientAPI` guard silently
    // suppressed every particle, since the tick only ever runs server-side).
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

    var particles = new SimpleParticleProperties(
      minQuantity: intensity * 6f,
      maxQuantity: intensity * 10f,
      color: ColorUtil.ToRgba(200, 80, 80, 80),
      minPos: minPos,
      maxPos: maxPos,
      // Lower upward velocity + shorter life ⇒ the plume only rises a short way
      // out of the vessel mouth instead of shooting toward the ceiling.
      minVelocity: new Vec3f(-0.15f, 0.5f, -0.15f),
      maxVelocity: new Vec3f(0.15f, 1.1f, 0.15f),
      lifeLength: 0.7f,
      gravityEffect: -0.05f,
      minSize: 0.15f,
      maxSize: 0.45f,
      model: EnumParticleModel.Quad
    )
    {
      OpacityEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, -120f),
      SizeEvolve = new EvolvingNatFloat(EnumTransformFunction.LINEAR, 1f),
    };

    Api.World.SpawnParticles(particles);
  }

  // Rotates a block-relative (x,z) around the block centre (0.5,0.5) to match the
  // shape's rotateY for this orientation (north 0, west 90, south 180, east 270).
  private void RotateXZ(ref float x, ref float z)
  {
    float dx = x - 0.5f;
    float dz = z - 0.5f;

    (float ndx, float ndz) = Block.Variant["side"] switch
    {
      "west" => (dz, -dx), // 90 degrees:  (x, z) -> (z, -x)
      "south" => (-dx, -dz), // 180 degrees: (x, z) -> (-x, -z)
      "east" => (-dz, dx), // 270 degrees: (x, z) -> (-z, x)
      _ => (dx, dz), // "north" or default 0 degrees
    };

    x = 0.5f + ndx;
    z = 0.5f + ndz;
  }

  private void ApplyPose()
  {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    var util = _animatable.animUtil;
    util.StopAnimation("idle");
    util.StopAnimation("filling");
    util.StopAnimation("pouring");

    // Pose tilts only apply once the vessel is built; during construction it
    // simply renders the partial mesh at rest via "idle".
    string code = (IsConstructed ? _opState : BessemerOpState.Normal) switch
    {
      BessemerOpState.Filling => "filling",
      BessemerOpState.Pouring => "pouring",
      _ => "idle",
    };

    util.StartAnimation(
      new AnimationMetaData
      {
        Animation = code,
        Code = code,
        // The whole vessel tilts — slow and heavy. Idle just holds it visible.
        AnimationSpeed = code == "idle" ? 1f : 0.6f,
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

  #region HUD

  /// <summary>
  /// The vessel is the big block the player looks at, so it carries the live
  /// operational readout (charge, refining progress, power, status). The actual
  /// state lives on the control brain, which builds the text for us.
  /// </summary>
  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);

    // While still under construction the RightClickConstructable interaction
    // help covers what to do next — don't show operational state yet.
    if (!IsConstructed)
      return;

    GetControl()?.AppendStructureState(forPlayer, dsc);
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    if (_controlPos != null)
    {
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
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    if (tree.HasAttribute("ctrlX"))
      _controlPos = new BlockPos(
        tree.GetInt("ctrlX"),
        tree.GetInt("ctrlY"),
        tree.GetInt("ctrlZ")
      );

    var prevState = _opState;
    _opState = (BessemerOpState)tree.GetInt("opState");
    _solidified = tree.GetBool("solidified");
    _chargeUnits = tree.GetInt("chargeUnits");

    if (Api?.Side == EnumAppSide.Client && prevState != _opState)
      ApplyPose();
  }

  #endregion
}

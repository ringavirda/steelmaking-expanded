using System.Text;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// A gas pipe section with a shut-off valve. The open/closed state is no longer
/// encoded as a separate block variant; it lives on the block entity and is
/// represented visually by holding the shape's <c>open</c> animation pose (driven
/// through the <see cref="BEBehaviorAnimatable"/> behavior). Gas flow is gated by
/// <see cref="IsConnectionBroken"/>, which the network re-evaluates whenever the
/// valve is toggled.
/// </summary>
[EntityRegister]
public class BlockEntityValve : BlockEntityPipe
{
  private bool _open;

  private BEBehaviorAnimatable? _animatable;
  private bool _animatorReady;

  /// <summary>Whether the valve is currently open (passing gas).</summary>
  public bool IsOpen() => _open;

  /// <summary>A closed valve breaks the network connection so gas cannot pass through.</summary>
  public override bool IsConnectionBroken() => !_open;

  #region Lifecycle

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();

    if (api is ICoreClientAPI capi && _animatable != null)
    {
      InitAnimator(capi);
      ApplyValvePose();
    }
  }

  private void InitAnimator(ICoreClientAPI capi)
  {
    Shape? shape = capi
      .Assets.TryGet(
        Block
          .Shape.Base.Clone()
          .WithPathPrefixOnce("shapes/")
          .WithPathAppendixOnce(".json")
      )
      ?.ToObject<Shape>();
    if (shape == null)
      return;

    // Rotation is applied by the renderer (per-orientation rotateX/Y from the
    // blocktype), not baked into the mesh — same approach as the bessemer parts.
    _animatable?.animUtil.InitializeAnimator(
      "gasvalve-" + Block.Variant["orientation"],
      shape,
      capi.Tesselator.GetTextureSource(Block),
      new Vec3f(Block.Shape.rotateX, Block.Shape.rotateY, Block.Shape.rotateZ)
    );

    // AnimatableRenderer only honours the Y component of that rotation vector;
    // rotateX/rotateZ are silently dropped. The vertical valve variants (ud/du) place
    // their pipe along rotateX=90, so the held "open" pose rendered flat (horizontal)
    // while the static closed mesh — rotated by the chunk tesselator — stayed vertical,
    // making an opening valve appear to snap sideways. Drive the full rotation through
    // the renderer's CustomTransform instead, replicating the exact model-space rotation
    // the tesselator bakes into a block's CompositeShape so every orientation matches.
    if (_animatable?.animUtil.renderer is { } renderer)
      renderer.CustomTransform = BuildShapeRotationTransform(
        Block.Shape.rotateX,
        Block.Shape.rotateY,
        Block.Shape.rotateZ
      );

    _animatorReady = true;
  }

  /// <summary>
  /// Builds the model→world matrix that matches how the chunk tesselator rotates a
  /// block's <see cref="Vintagestory.API.Common.CompositeShape"/>:
  /// <c>T(centre) · RotateXYZ · T(-centre)</c> about the block centre (0.5, 0.5, 0.5),
  /// with X→Y→Z applied in the same order as <c>MeshData.Rotate</c>. Assigned to the
  /// animator renderer's <c>CustomTransform</c> so the animated pose lines up with the
  /// static block mesh in every orientation, including the vertical ones.
  /// </summary>
  private static float[] BuildShapeRotationTransform(
    float rotXDeg,
    float rotYDeg,
    float rotZDeg
  )
  {
    float[] rotation = Mat4f.Create();
    Mat4f.RotateXYZ(
      rotation,
      rotXDeg * GameMath.DEG2RAD,
      rotYDeg * GameMath.DEG2RAD,
      rotZDeg * GameMath.DEG2RAD
    );

    float[] transform = Mat4f.Create();
    Mat4f.Identity(transform);
    Mat4f.Translate(transform, transform, 0.5f, 0.5f, 0.5f);
    Mat4f.Mul(transform, transform, rotation);
    Mat4f.Translate(transform, transform, -0.5f, -0.5f, -0.5f);
    return transform;
  }

  /// <summary>
  /// A wrench rotates the valve via <c>ExchangeBlock</c>, which swaps the block to
  /// the new orientation variant but keeps this block entity alive — so
  /// <see cref="Initialize"/> never re-runs and the animator stays bound to the
  /// <em>original</em> orientation's renderer rotation. The static mesh
  /// re-tessellates into the new facing while the held "open" pose kept rendering
  /// in the old one, making the valve appear to flip between orientations as it
  /// was toggled. Re-bind the animator to the new block's rotation (the core
  /// <c>InitializeAnimator</c> disposes the stale renderer first) and restore the
  /// current pose. Fires on both sides; only the client has an animator.
  /// </summary>
  public override void OnExchanged(Block block)
  {
    base.OnExchanged(block);

    if (Api is ICoreClientAPI capi && _animatable != null)
    {
      _animatorReady = false;
      InitAnimator(capi);
      ApplyValvePose();
    }
  }

  /// <summary>Server-side toggle of the valve's open state.</summary>
  public void ToggleOpen()
  {
    _open = !_open;
    MarkDirty(true);
  }

  private void ApplyValvePose()
  {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    if (_open)
      _animatable.animUtil.StartAnimation(
        new AnimationMetaData
        {
          Animation = "open",
          Code = "open",
          AnimationSpeed = 2.5f,
          EaseInSpeed = 8f,
          EaseOutSpeed = 8f,
        }.Init()
      );
    else
      _animatable.animUtil.StopAnimation("open");
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(
      Lang.Get(
        "ppex:valve-state",
        Lang.Get(_open ? "ppex:valve-open" : "ppex:valve-closed")
      )
    );
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("valveOpen", _open);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    bool prev = _open;
    _open = tree.GetBool("valveOpen");
    if (Api?.Side == EnumAppSide.Client && prev != _open)
      ApplyValvePose();
  }

  #endregion
}

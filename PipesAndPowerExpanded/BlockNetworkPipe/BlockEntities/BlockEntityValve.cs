using System;
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
/// A manually-toggled in-line valve. Open, it is a normal pipe node and the run flows straight
/// through it as one network; closed, it severs the run at its own cell
/// (<see cref="IsConnectionBroken"/>), splitting it in two. Toggling re-walks the graph. The
/// state is shown by holding the shape's <c>open</c> animation pose.
/// </summary>
[EntityRegister]
public class BlockEntityValve : BlockEntityPipe
{
  private bool _open;

  private BEBehaviorAnimatable? _animatable;
  private bool _animatorReady;
  private string? _animatorOrientation;

  /// <summary>Whether the valve is currently open (letting the run flow through it).</summary>
  public bool IsOpen() => _open;

  /// <summary>A closed valve severs the run at its cell; open, it is a normal in-line node.</summary>
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
    if (!_open)
      Pressure = 0f;
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

    // Rotation is applied by the renderer (per-orientation), not baked into the mesh.
    _animatable?.animUtil.InitializeAnimator(
      "gasvalve-" + Block.Variant["orientation"],
      shape,
      capi.Tesselator.GetTextureSource(Block),
      new Vec3f(Block.Shape.rotateX, Block.Shape.rotateY, Block.Shape.rotateZ)
    );

    // AnimatableRenderer only honours the Y rotation; rotateX/Z are dropped, so the vertical
    // valve variants (ud/du, pipe along rotateX=90) rendered the "open" pose flat while the
    // static mesh stayed vertical. Drive the full rotation through CustomTransform instead.
    if (_animatable?.animUtil.renderer is { } renderer)
      renderer.CustomTransform = BuildShapeRotationTransform(
        Block.Shape.rotateX,
        Block.Shape.rotateY,
        Block.Shape.rotateZ
      );

    _animatorOrientation = Block.Variant["orientation"];
    _animatorReady = true;
  }

  /// <summary>
  /// Builds the model→world matrix matching how the chunk tesselator rotates a CompositeShape:
  /// <c>T(centre) · RotateXYZ · T(-centre)</c> about (0.5, 0.5, 0.5), X→Y→Z order. Assigned to
  /// the renderer's <c>CustomTransform</c> so the animated pose lines up in every orientation.
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
  /// A wrench rotates the valve via <c>ExchangeBlock</c>, which keeps this BE alive so
  /// <see cref="Initialize"/> never re-runs and the animator stays bound to the original
  /// orientation. Re-bind the animator to the new block's rotation and restore the pose.
  /// </summary>
  public override void OnExchanged(Block block)
  {
    base.OnExchanged(block);

    if (Api is ICoreClientAPI capi && _animatable != null)
    {
      // Only re-init on a real orientation change. A network re-walk can re-exchange to an
      // equivalent variant (ns<->sn); re-initing each time would reset the "open" pose.
      if (
        _animatorReady
        && _animatorOrientation == block.Variant["orientation"]
      )
        return;

      _animatorReady = false;
      InitAnimator(capi);
      ApplyValvePose();
    }
  }

  /// <summary>Server-side toggle of the valve's open state. Re-walks the network so the
  /// change in connectivity (open rejoins the two sides, closed severs them) takes effect
  /// immediately.</summary>
  public void ToggleOpen()
  {
    _open = !_open;
    MarkDirty(true);

    // RemoveNode runs fracture detection (closing splits the run); AddNode re-merges both
    // sides when open, or re-isolates the cell when closed.
    if (
      Api?.Side == EnumAppSide.Server
      && NetworkSystem != null
      && Api.World?.BlockAccessor is { } ba
    )
    {
      NetworkSystem.RemoveNode(ba, Pos);
      NetworkSystem.AddNode(ba, Pos, NetworkType);
    }
    if (!_open)
    {
      Pressure = 0f;
      DiscardNetworkPool();
    }
  }

  /// <summary>
  /// Drops any cached/persisted network pool. Closing severs the cell without a broadcast, so the
  /// pressurised state it cached while open would otherwise serialise and be restored into the
  /// isolated cell on reload, bursting it. Clearing keeps closed-valve saves empty.
  /// </summary>
  private void DiscardNetworkPool()
  {
    _savedNetworkState = null;
    _networkState = null;
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
    dsc.AppendLine(
      Lang.Get(
        "ppex:valve-state",
        Lang.Get(_open ? "ppex:valve-open" : "ppex:valve-closed")
      )
    );
    // Open, the base pipe info reports what flows through; closed, it reads empty.
    base.GetBlockInfo(forPlayer, dsc);
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
    // Closed at save time → isolated cell holds nothing. Drop any persisted pool before
    // Initialize captures it for restore, so a stale pressurised state can't burst it on load.
    if (!_open)
      DiscardNetworkPool();
    if (Api?.Side == EnumAppSide.Client && prev != _open)
      ApplyValvePose();
  }

  #endregion
}

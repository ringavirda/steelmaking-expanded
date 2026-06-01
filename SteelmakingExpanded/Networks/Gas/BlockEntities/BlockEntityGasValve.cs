using System.Text;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// A gas pipe section with a shut-off valve. The open/closed state is no longer
/// encoded as a separate block variant; it lives on the block entity and is
/// represented visually by holding the shape's <c>open</c> animation pose (driven
/// through the <see cref="BEBehaviorAnimatable"/> behavior). Gas flow is gated by
/// <see cref="IsConnectionBroken"/>, which the network re-evaluates whenever the
/// valve is toggled.
/// </summary>
public class BlockEntityGasValve : BlockEntityGasPipe
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
    _animatorReady = true;
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
        "smex:valve-state",
        Lang.Get(_open ? "smex:valve-open" : "smex:valve-closed")
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

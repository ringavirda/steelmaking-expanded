using System.Text;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>
/// Block entity for the blast-furnace tap. Tracks the pouring state (shown via the
/// shape's open animation pose) and accepts molten metal pushed from the furnace to
/// hand down into the canal start beneath it.
/// </summary>
[EntityRegister]
public class BlockEntityBlastFurnaceTap : BlockEntity
{
  /// <summary>Whether the tap is currently open and pouring.</summary>
  public bool IsPouring { get; private set; } = false;

  private BEBehaviorAnimatable? _animatable;
  private bool _animatorReady;

  #region Lifecycle

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _animatable = GetBehavior<BEBehaviorAnimatable>();

    if (api is ICoreClientAPI capi && _animatable != null)
    {
      InitAnimator(capi);
      ApplyPourPose();
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

    _animatable?.animUtil.InitializeAnimator(
      "blastfurnacetap-" + Block.Variant["side"],
      shape,
      capi.Tesselator.GetTextureSource(Block),
      new Vec3f(0, Block.Shape.rotateY, 0)
    );
    _animatorReady = true;
  }

  /// <summary>Toggles the tap open/closed and updates its pour pose.</summary>
  public void TogglePouring()
  {
    IsPouring = !IsPouring;
    MarkDirty(true);
  }

  private void ApplyPourPose()
  {
    if (Api is not ICoreClientAPI || _animatable == null || !_animatorReady)
      return;

    if (IsPouring)
      _animatable.animUtil.StartAnimation(
        new AnimationMetaData
        {
          Animation = "open",
          Code = "open",
          AnimationSpeed = 1.5f,
          EaseInSpeed = 6f,
          EaseOutSpeed = 6f,
        }.Init()
      );
    else
      _animatable.animUtil.StopAnimation("open");
  }

  #endregion

  #region Serialization

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("isPouring", IsPouring);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    bool prev = IsPouring;
    IsPouring = tree.GetBool("isPouring");
    if (Api?.Side == EnumAppSide.Client && prev != IsPouring)
      ApplyPourPose();
  }

  #endregion

  #region HUD

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(
      Lang.Get(
        "smex:tap-state",
        Lang.Get(IsPouring ? "smex:tap-open" : "smex:tap-closed")
      )
    );
  }

  #endregion

  #region Pouring

  /// <summary>
  /// Pushes <paramref name="moltenMetal"/> into the canal start beneath the spout.
  /// Returns the amount the canal actually consumed (0 if not pouring or nothing was transferred).
  /// </summary>
  public int TryPourMetal(ItemStack moltenMetal, float temperature)
  {
    if (!IsPouring || moltenMetal == null)
      return 0;

    BlockFacing facing = BlockFacing.FromCode(Block.Variant["side"]);
    if (facing == null)
      return 0;

    BlockPos startPos = Pos.AddCopy(facing.Opposite).DownCopy();

    if (
      Api.World.BlockAccessor.GetBlockEntity(startPos)
      is not BlockEntityMoltenCanalStart startCanal
    )
      return 0;

    // Use the looser predicate so a brim-full start still receives the pour and
    // soaks its heat (keeping it molten) instead of stalling and cooling.
    if (!startCanal.CanReceiveOrSoak(moltenMetal))
      return 0;

    startCanal.BeginFill(Pos.ToVec3d());

    // amount is passed by ref; ReceiveLiquidMetal decrements it by what the
    // network accepted, so after the call it holds the LEFTOVER, not the
    // consumed amount. Return the difference (= actually accepted).
    int requested = moltenMetal.StackSize;
    int amount = requested;
    startCanal.ReceiveLiquidMetal(moltenMetal, ref amount, temperature);
    startCanal.OnPourOver();

    return requested - amount;
  }

  #endregion
}

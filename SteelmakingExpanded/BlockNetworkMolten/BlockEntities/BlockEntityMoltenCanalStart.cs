using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockNetworkMolten.BlockEntities;

/// <summary>
/// Block entity for the molten-canal start. Acts as the network's
/// <see cref="ILiquidMetalSink"/>: liquid metal poured here (from a tap above or a
/// crucible) enters this cell and then flows down the canal run. Per-cell state is
/// persisted by the base <see cref="BlockEntityMoltenCanal"/>.
/// </summary>
[EntityRegister]
public class BlockEntityMoltenCanalStart
  : BlockEntityMoltenCanal,
    ILiquidMetalSink
{
  /// <summary> Start block by itself has higher capacity. </summary>
  public override int MaxUnitCapacity =>
    SmexValues.CanalDefaultUnitCapacity * 2;

  // Throttle for the molten-pour sound as metal enters here.
  private long _lastPourSoundMs;

  // The start is a source fitting — it must keep accepting/passing metal, so it
  // never clogs like a plain canal run.
  protected override bool SolidifiesWhenCold => false;

  #region ILiquidMetalSink

  /// <inheritdoc/>
  public bool CanReceiveAny => !Solidified && CellAmount < MaxUnitCapacity;

  /// <inheritdoc/>
  public bool CanReceive(ItemStack metal) =>
    !Solidified
    && CellAmount < MaxUnitCapacity
    && (CellAmount <= 0f || CellMetalType == metal.Collectible.Code.ToString());

  /// <inheritdoc/>
  public void BeginFill(Vec3d hitPosition) { }

  /// <inheritdoc/>
  public void OnPourOver() { }

  /// <inheritdoc/>
  public void ReceiveLiquidMetal(
    ItemStack metal,
    ref int amount,
    float temperature
  )
  {
    if (Api?.Side == EnumAppSide.Client)
    {
      // Show the pour immediately; the server confirms the real fill on next sync.
      ShowPendingFill(amount);
      amount = 0;
      return;
    }

    if (!CanReceive(metal))
      return;

    float accepted = PushMetal(amount, metal, Api!.World);
    amount -= (int)accepted;

    if (accepted > 0f)
      SmexSounds.PlayThrottled(
        Api,
        Pos,
        SmexSounds.PourMetal,
        ref _lastPourSoundMs,
        2000,
        0.6f
      );
  }

  #endregion
}

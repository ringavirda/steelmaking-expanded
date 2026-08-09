using System.Text;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockNetworkMolten.BlockEntities;

/// <summary>
/// Block entity for the molten-canal start. Acts as the network's
/// <see cref="ILiquidMetalSink"/>: liquid metal poured here (from a tap above or a
/// crucible) enters this cell and then flows down the canal run. Per-cell state is
/// persisted by the base <see cref="BlockEntityMoltenCanal"/>.
/// </summary>
[BlockEntityRegister]
public class BlockEntityMoltenCanalStart
  : BlockEntityMoltenCanal,
    ILiquidMetalSink {
  /// <summary> Start block by itself has higher capacity. </summary>
  public override int MaxUnitCapacity =>
    SmexValues.CanalDefaultUnitCapacity * 2;

  // Throttle for the molten-pour sound as metal enters here.
  private long _lastPourSoundMs;

  // Running tally of metal accepted here, shown in the block info while pouring is active. Cleared
  // once no new metal has arrived for PourTallyTimeoutMs (server tracks the timestamp; the count is
  // synced to clients for display).
  private const long PourTallyTimeoutMs = 5000;
  private int _pourTally;
  private long _lastPourMs;

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    // Server clears the pour tally once it goes idle; clients only render the synced count.
    if (api.Side == EnumAppSide.Server)
      RegisterGameTickListener(OnPourTallyTick, 1000);
  }

  private void OnPourTallyTick(float dt) {
    if (
      _pourTally > 0
      && Api.World.ElapsedMilliseconds - _lastPourMs > PourTallyTimeoutMs
    ) {
      _pourTally = 0;
      MarkDirty();
    }
  }

  #region ILiquidMetalSink

  /// <inheritdoc/>
  public bool CanReceiveAny => !Solidified && CellAmount < MaxUnitCapacity;

  /// <inheritdoc/>
  public bool CanReceive(ItemStack metal) =>
    !Solidified
    && CellAmount < MaxUnitCapacity
    && (CellAmount <= 0f || CellMetalType == metal.Collectible.Code.ToString());

  /// <summary>
  /// Looser than <see cref="CanReceive"/>: also true when the cell is FULL of the same metal, so
  /// the furnace tap keeps pouring (soaking heat via <see cref="ReceiveLiquidMetal"/>) instead of
  /// letting a brim-full start cool to a plug.
  /// </summary>
  public bool CanReceiveOrSoak(ItemStack metal) =>
    !Solidified
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
  ) {
    if (Api?.Side == EnumAppSide.Client) {
      // Show the pour immediately; the server confirms the real fill on next sync.
      ShowPendingFill(amount);
      amount = 0;
      return;
    }

    string type = metal.Collectible.Code.ToString();
    // Reject only on solidification or metal-type mismatch - a FULL cell still soaks heat below,
    // so a start fed by the furnace stays molten even when it can't accept more volume.
    if (Solidified || (CellAmount > 0f && CellMetalType != type))
      return;

    // Use the pour temperature directly (the tap passes the live tap temp).
    int accepted = PushMetalRaw(amount, type, temperature, Api!.World);
    amount -= accepted;

    // Unaccepted overflow still bathes the cell in hot metal - soak that heat so it never plugs.
    bool soaked = amount > 0 && SoakHeat(Api.World, temperature);

    if (accepted > 0) {
      _pourTally += accepted;
      _lastPourMs = Api.World.ElapsedMilliseconds;
      MarkDirty();
    }

    if (accepted > 0 || soaked)
      ExSounds.PlayThrottled(
        Api,
        Pos,
        ExSounds.PourMetal,
        ref _lastPourSoundMs,
        2000,
        0.6f
      );
  }

  #endregion

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetInt("pourTally", _pourTally);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _pourTally = tree.GetInt("pourTally");
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);

    // Live feedback while metal is flowing in; the tally self-clears after a few idle seconds.
    if (_pourTally > 0)
      dsc.AppendLine(Lang.Get("smex:canalstart-pouredtally", _pourTally));
  }
}

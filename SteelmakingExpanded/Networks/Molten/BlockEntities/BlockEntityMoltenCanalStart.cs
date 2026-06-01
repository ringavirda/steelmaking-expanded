using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Molten.BlockEntities;

/// <summary>
/// Block entity for the molten-canal start. Acts as the network's <see cref="ILiquidMetalSink"/>:
/// liquid metal poured here (from a tap above or a crucible) is pushed into the
/// canal network and persisted across reloads.
/// </summary>
public class BlockEntityMoltenCanalStart
  : BlockEntityMoltenCanal,
    ILiquidMetalSink
{
  /// <summary>Metal accepted directly at this node before it is pushed into the network.</summary>
  public float OwnCurrentAmount { get; set; } = 0f;

  // Throttle for the molten-pour sound as metal enters the network here.
  private long _lastPourSoundMs;

  #region Persistence hooks

  protected override bool IsNetworkStateMeaningful(object? state) =>
    state is MoltenNetworkState s && s.CurrentAmount > 0f;

  protected override object? DeserializeNetworkState(ITreeAttribute tree)
  {
    if (!tree.GetBool("hasMoltenState"))
      return null;

    // Migrate old "Iron"/"Steel"/"Slag" values to full AssetLocation strings.
    string rawType = tree.GetString("moltenType", "");
    string metalType = rawType switch
    {
      "Iron" => "game:ingot-iron",
      "Steel" => "game:ingot-steel",
      "Slag" => "smex:slag",
      _ => rawType,
    };

    return new MoltenNetworkState
    {
      CurrentAmount = tree.GetFloat("moltenAmount"),
      CurrentTemperature = tree.GetFloat("moltenTemp", 1300f),
      MetalType = metalType,
      // MetalStack is null here; OnTick reconstructs it lazily once the world is available.
    };
  }

  protected override void SerializeNetworkState(
    ITreeAttribute tree,
    object? state
  )
  {
    if (state is MoltenNetworkState s && s.CurrentAmount > 0f)
    {
      tree.SetBool("hasMoltenState", true);
      tree.SetFloat("moltenAmount", s.CurrentAmount);
      tree.SetFloat("moltenTemp", s.CurrentTemperature);
      tree.SetString("moltenType", s.MetalType);
    }
    else
    {
      tree.SetBool("hasMoltenState", false);
    }
  }

  #endregion

  #region ILiquidMetalSink

  /// <inheritdoc/>
  public bool CanReceiveAny
  {
    get
    {
      if (Api?.Side == EnumAppSide.Client)
        return _clientMaxAmount == 0f
          || _clientCurrentAmount < _clientMaxAmount;

      return NetworkSystem?.GetNetworkAt(Pos) is MoltenNetwork net
        && (
          net.State is not MoltenNetworkState s || s.CurrentAmount < s.MaxAmount
        );
    }
  }

  /// <inheritdoc/>
  public bool CanReceive(ItemStack metal)
  {
    if (Api?.Side == EnumAppSide.Client)
    {
      if (_clientMaxAmount == 0f)
        return true;
      return _clientCurrentAmount < _clientMaxAmount
        && _clientMetalType == metal.Collectible.Code.ToString();
    }

    return NetworkSystem?.GetNetworkAt(Pos) is MoltenNetwork net
      && (
        net.State is not MoltenNetworkState s
        || (
          s.CurrentAmount < s.MaxAmount
          && s.MetalType == metal.Collectible.Code.ToString()
        )
      );
  }

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
      _pendingFillAmount = amount;
      UpdateRenderer();
      amount = 0;
      return;
    }

    if (
      !CanReceive(metal)
      || NetworkSystem?.GetNetworkAt(Pos) is not MoltenNetwork network
    )
      return;

    float accepted = network.TryPushMetal(
      amount,
      metal,
      Api!.World,
      Api!.World.BlockAccessor
    );
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

    MarkDirty(true);
  }

  #endregion
}

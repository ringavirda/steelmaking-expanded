using System;
using System.Text;
using SteelmakingExpanded.Networks.Gas.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// Block entity for the heated-intake block. Transfers gas from the intake
/// network to the output network each tick, and boosting temperature when a
/// burning coal pile sits below. May convert the gas to <c>Exhaust</c> if low
/// grade coal used.
/// </summary>
public class BlockEntityGasHeatedIntake : BlockEntityGasPipe
{
  private long _tickId;
  private float _lastTransferVolume;
  private string _lastTransferType = "Air";

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      _tickId = RegisterGameTickListener(OnTick, 1000);
  }

  private void OnTick(float dt)
  {
    if (Block is not BlockGasHeatedIntake pipe)
      return;
    string orient = pipe.Variant["orientation"];
    if (string.IsNullOrEmpty(orient) || orient.Length < 2)
      return;

    BlockFacing outFace = BlockFacing.FromFirstLetter(orient[0].ToString());
    BlockFacing inFace = BlockFacing.FromFirstLetter(orient[1].ToString());
    if (inFace == null || outFace == null)
      return;

    BlockPos inPos = Pos.AddCopy(inFace);
    BlockPos outPos = Pos.AddCopy(outFace);

    var inNet = NetworkSystem?.GetNetworkAt(inPos) as GasNetwork;
    var outNet = NetworkSystem?.GetNetworkAt(outPos) as GasNetwork;

    float transfer = 0f;
    string outType = "Air";

    if (
      inNet != null
      && outNet != null
      && inNet.State is GasNetworkState inState
    )
    {
      float outCapacity = outNet.Nodes.Count;
      transfer = Math.Min(
        inState.CurrentVolume,
        outCapacity - (outNet.State?.CurrentVolume ?? 0f)
      );
      transfer = Math.Min(transfer, 2.0f);

      if (transfer > 0)
      {
        // Check for burning coal below.
        bool isFiring = false;
        bool isAnthracite = false;
        if (
          Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy())
          is BlockEntityCoalPile pile
        )
        {
          if (pile.IsBurning)
          {
            isFiring = true;
            string? path = pile.inventory
              ?[0]
              ?.Itemstack
              ?.Collectible
              ?.Code
              ?.Path;
            if (path != null && path.Contains("anthracite"))
              isAnthracite = true;
          }
        }

        // Capture source properties.
        string gasType = inState.GasType;
        float temperature = inState.SourceTemperature;

        if (isFiring)
        {
          if (!isAnthracite || gasType == "Exhaust")
            gasType = "Exhaust";
          if (temperature < 400f)
            temperature = 400f;
        }

        outType = gasType;

        // Delegate state changes and broadcasting to the networks.
        inNet.TryConsumeGas(transfer, Api.World.BlockAccessor);
        outNet.TryProduceGas(
          transfer,
          temperature,
          gasType,
          Api.World.BlockAccessor
        );
      }
    }

    if (
      Math.Abs(_lastTransferVolume - transfer) > 0.01f
      || _lastTransferType != outType
    )
    {
      _lastTransferVolume = transfer;
      _lastTransferType = outType;
      MarkDirty(true);
    }
  }

  public override void OnBlockRemoved()
  {
    base.OnBlockRemoved();
    if (_tickId != 0)
    {
      UnregisterGameTickListener(_tickId);
      _tickId = 0;
    }
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("lastTransferVolume", _lastTransferVolume);
    tree.SetString("lastTransferType", _lastTransferType);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _lastTransferVolume = tree.GetFloat("lastTransferVolume");
    _lastTransferType = tree.GetString("lastTransferType", "Air");
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(
      Lang.Get(
        "smex:gasheated-info-passing",
        _lastTransferVolume,
        _lastTransferType
      )
    );
  }
}

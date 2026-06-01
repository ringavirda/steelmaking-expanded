using System;
using System.Linq;
using System.Text;
using SteelmakingExpanded.Networks.Gas.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// Block entity for the gas blower.  Bridges two adjacent gas networks by
/// transferring gas from the intake side to the output side at a rate
/// proportional to the attached mechanical network speed.  When spinning fast
/// enough, air is converted to Blast.
/// </summary>
public class BlockEntityGasBlower : BlockEntityGasPipe
{
  private const float MinRPSForBlast = 1.5f;

  private long _tickId;
  private float _lastTransferVolume;
  private string _lastTransferType = "Air";
  private long _lastBlowSoundMs;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      _tickId = RegisterGameTickListener(OnTick, 1000);
  }

  private void OnTick(float dt)
  {
    if (Block is not BlockGasBlower pipe)
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

    var gasNetA = NetworkSystem?.GetNetworkAt(inPos) as GasNetwork;
    var gasNetB = NetworkSystem?.GetNetworkAt(outPos) as GasNetwork;

    // Determine which side is intake (the one that has an intake block in its network).
    bool aHasIntake =
      gasNetA?.Nodes.Any(p =>
        Api.World.BlockAccessor.GetBlockEntity(p) is BlockEntityGasIntake
      )
      ?? false;
    bool bHasIntake =
      gasNetB?.Nodes.Any(p =>
        Api.World.BlockAccessor.GetBlockEntity(p) is BlockEntityGasIntake
      )
      ?? false;

    if (aHasIntake && bHasIntake)
    {
      _lastTransferVolume = 0;
      _lastTransferType = "Error: Multiple Intakes";
      MarkDirty(true);
      return;
    }

    GasNetwork? inNet = aHasIntake ? gasNetA : (bHasIntake ? gasNetB : null);
    GasNetwork? outNet = aHasIntake ? gasNetB : (bHasIntake ? gasNetA : null);

    float transfer = 0f;
    string outType = "Air";

    float speed = 0f;
    var mpBlower =
      Behaviors.FirstOrDefault(b => b is BEBehaviorMPBase) as BEBehaviorMPBase;
    if (mpBlower?.Network != null)
      speed = Math.Abs(mpBlower.Network.Speed * mpBlower.GearedRatio);

    if (
      inNet != null
      && outNet != null
      && speed > 0.1f
      && inNet.State is GasNetworkState inState
    )
    {
      float outCapacity = outNet.Nodes.Count;
      transfer = Math.Min(
        Math.Min(1.0f, inState.CurrentVolume),
        outCapacity - (outNet.State?.CurrentVolume ?? 0f)
      );

      if (transfer > 0)
      {
        string gasType = inState.GasType;
        if (speed >= MinRPSForBlast && gasType == "Air")
          gasType = "Blast";
        float temperature = inState.SourceTemperature;
        outType = gasType;

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
      speed > MinRPSForBlast
      && outNet?.State is GasNetworkState outState
      && outState.GasType == "Air"
    )
      outState.GasType = GasNetworkState.GetHigherPriorityGas(
        outState.GasType,
        "Blast"
      );

    if (transfer > 0f)
      SmexSounds.PlayThrottled(
        Api,
        Pos,
        SmexSounds.Bellows,
        ref _lastBlowSoundMs,
        3000,
        0.4f
      );

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
        "smex:gasblower-info-blowing",
        _lastTransferVolume,
        _lastTransferType
      )
    );
  }
}

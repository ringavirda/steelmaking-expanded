using System;
using System.Text;
using BlockNetworkLib;
using SteelmakingExpanded.Networks.Gas.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// Block entity for the pressure-relief valve.  When the input network exceeds a
/// configurable fill threshold, overflow is pushed into the output network.
/// </summary>
public class BlockEntityGasPressureValve : BlockEntityGasPipe, IGasPressureValve
{
  private long _tickId;
  private float _lastOverflowVolume;
  private int _thresholdPercent = 80;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      _tickId = RegisterGameTickListener(OnTick, 1000);
  }

  /// <summary>Cycles the relief threshold in 20% steps (0 → 20 → … → 100 → 0).</summary>
  public void CycleThreshold()
  {
    _thresholdPercent += 20;
    if (_thresholdPercent > 100)
      _thresholdPercent = 0;
    MarkDirty(true);
  }

  private void OnTick(float dt)
  {
    if (
      Block is not BlockGasPressureValve valve
      || string.IsNullOrEmpty(valve.Orientation)
      || valve.Orientation.Length < 2
    )
      return;

    // Orientation: first char = input side, second char = output side.
    BlockFacing inFace = BlockFacing.FromFirstLetter(valve.Orientation[0]);
    BlockFacing outFace = BlockFacing.FromFirstLetter(valve.Orientation[1]);
    if (inFace == null || outFace == null)
      return;

    var inNet = NetworkSystem?.GetNetworkAt(Pos.AddCopy(inFace)) as GasNetwork;
    var outNet =
      NetworkSystem?.GetNetworkAt(Pos.AddCopy(outFace)) as GasNetwork;

    float transfer = 0f;

    if (inNet != null && inNet.State is GasNetworkState inState)
    {
      float threshold = inNet.Nodes.Count * (_thresholdPercent / 100f);
      if (inState.CurrentVolume > threshold)
      {
        float overflow = inState.CurrentVolume - threshold;
        float outCapacity = outNet?.Nodes.Count ?? 0f;
        float outFree = outCapacity - (outNet?.State?.CurrentVolume ?? 0f);
        transfer = Math.Min(overflow, outFree);

        if (transfer > 0 && outNet != null)
        {
          // Capture source properties before consuming.
          string gasType = inState.GasType;
          float temperature = inState.SourceTemperature;

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
    }

    if (Math.Abs(_lastOverflowVolume - transfer) > 0.01f)
    {
      _lastOverflowVolume = transfer;
      MarkDirty(true);
    }
    else
    {
      _lastOverflowVolume = transfer;
    }
  }

  /// <inheritdoc/>
  public BlockFacing? GetPriorityFace()
  {
    if (
      Block is BlockGasPipe pipe
      && pipe.Orientation != null
      && pipe.Orientation.Length >= 2
    )
      return BlockFacing.FromCode(pipe.Orientation[1].ToString());
    return null;
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
    tree.SetInt("thresholdPercent", _thresholdPercent);
    tree.SetFloat("lastOverflowVolume", _lastOverflowVolume);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _thresholdPercent = tree.GetInt("thresholdPercent", 80);
    _lastOverflowVolume = tree.GetFloat("lastOverflowVolume");
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    dsc.AppendLine(
      Lang.Get("smex:gaspressurevalve-info-threshold", _thresholdPercent)
    );
    dsc.AppendLine(
      Lang.Get("smex:gaspressurevalve-info-overflow", _lastOverflowVolume)
    );
  }
}

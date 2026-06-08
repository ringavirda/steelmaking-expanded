using System;
using System.Text;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// Block entity for the pressure-relief valve. It vents the gas pool of either
/// adjacent network to atmosphere whenever that network's pressure exceeds the
/// valve's own material rating (copper 1.5 / bronze·brass 3 / iron 5 / steel 15 atm),
/// bleeding volume until the pressure settles back to the rating — the controlled
/// relief that keeps a run below its pipes' burst threshold.
/// </summary>
[EntityRegister]
public class BlockEntityPressureValve : BlockEntityPipe, IPressureValve
{
  private long _tickId;
  private float _lastVentVolume;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      _tickId = RegisterGameTickListener(OnTick, 1000);
  }

  private void OnTick(float dt)
  {
    if (
      Block is not BlockPressureValve valve
      || string.IsNullOrEmpty(valve.Orientation)
      || valve.Orientation.Length < 2
    )
      return;

    float rating = valve.BurstPressure;
    BlockFacing inFace = BlockFacing.FromFirstLetter(valve.Orientation[0]);
    BlockFacing outFace = BlockFacing.FromFirstLetter(valve.Orientation[1]);

    float vented = VentIfOver(inFace, rating) + VentIfOver(outFace, rating);

    if (Math.Abs(_lastVentVolume - vented) > 0.01f)
    {
      _lastVentVolume = vented;
      MarkDirty(true);
    }
    else
    {
      _lastVentVolume = vented;
    }
  }

  /// <summary>Vents the gas-pool excess of the network on <paramref name="face"/> down to the rating.</summary>
  private float VentIfOver(BlockFacing? face, float rating)
  {
    if (face == null)
      return 0f;
    if (NetworkSystem?.GetNetworkAt(Pos.AddCopy(face)) is not PipeNetwork net)
      return 0f;
    if (net.State == null || net.State.MaxVolume <= 0f)
      return 0f;

    float allowed = rating * net.State.MaxVolume;
    if (net.State.CurrentVolume <= allowed)
      return 0f;

    float excess = net.State.CurrentVolume - allowed;
    return net.TryConsumeGas(excess, Api.World.BlockAccessor);
  }

  /// <inheritdoc/>
  public BlockFacing? GetPriorityFace()
  {
    if (
      Block is BlockPipe pipe
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

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    float rating = Block is BlockPressureValve v ? v.BurstPressure : 0f;
    dsc.AppendLine(Lang.Get("ppex:gaspressurevalve-info-rating", rating));
    if (_lastVentVolume > 0f)
      dsc.AppendLine(
        Lang.Get("ppex:gaspressurevalve-info-overflow", _lastVentVolume)
      );
  }
}

using System;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Networks.Gas.BlockEntities;

/// <summary>
/// Block entity for the air intake. Each second it injects a fixed volume of
/// ambient-temperature air into its gas network.
/// </summary>
public class BlockEntityGasIntake : BlockEntityGasPipe
{
  private long _tickId;
  private float _lastVolume = 0f;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server)
      _tickId = RegisterGameTickListener(OnTick, 1000);
  }

  private void OnTick(float dt)
  {
    float temp = Api.World.BlockAccessor.GetClimateAt(Pos)?.Temperature ?? 20f;
    bool produced = TryProduceGas(2.0f, temp, "Air");

    float currentVol = produced ? 2.0f : 0f;
    if (Math.Abs(_lastVolume - currentVol) > 0.01f)
    {
      _lastVolume = currentVol;
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
}

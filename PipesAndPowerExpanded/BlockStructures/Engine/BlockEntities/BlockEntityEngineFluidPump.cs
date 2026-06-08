using System;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Cornish-engine sub-machine: a water pump. While powered, if the network on its
/// bottom face contains a fluid intake sitting on water, it injects water into both
/// the bottom and left networks at a pressure proportional to the engine power.
/// </summary>
[EntityRegister]
public class BlockEntityEngineFluidPump : BlockEntityEngineSubmachine
{
  private bool _sourceAvailable;

  public override float PowerDemand =>
    Engine != null && _sourceAvailable ? 1f : 0f;

  protected override void DoWork(float power, float dt)
  {
    var ba = Api.World.BlockAccessor;
    PipeNetwork? bottomNet = NetworkAt(Pos.DownCopy());
    PipeNetwork? leftNet = NetworkAt(Pos.AddCopy(LeftFace));

    _sourceAvailable = HasWaterSource(bottomNet);
    if (power <= 0f || !_sourceAvailable)
      return;

    float power01 = power / Math.Max(0.01f, Engine?.MaxPower ?? 1f);
    float pressure = PpexValues.PumpMaxPressure * power01;
    float amount = PpexValues.PumpWaterPerPower * power * dt;

    bottomNet?.TryProduceLiquid(amount, 20f, pressure, ba);
    leftNet?.TryProduceLiquid(amount, 20f, pressure, ba);
  }

  /// <summary>True when the network has a fluid intake reporting an available water source.</summary>
  private bool HasWaterSource(PipeNetwork? net)
  {
    if (net == null)
      return false;
    var ba = Api.World.BlockAccessor;
    foreach (var p in net.Nodes)
    {
      if (
        ba.GetBlockEntity(p) is BlockEntityFluidIntake intake
        && intake.HasWater
      )
        return true;
    }
    return false;
  }
}

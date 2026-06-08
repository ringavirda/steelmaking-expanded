using System;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine;

namespace SteelmakingExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Cornish-engine sub-machine: an air compressor. While powered it injects air into
/// its left network at a pressure proportional to the engine power; once that air
/// crosses the blast threshold (≥ 3 atm) it counts as blast. The mod's only air source.
/// </summary>
[EntityRegister]
public class BlockEntityEngineAirBlower : BlockEntityEngineSubmachine
{
  protected override void DoWork(float power, float dt)
  {
    if (power <= 0f)
      return;
    PipeNetwork? leftNet = NetworkAt(Pos.AddCopy(LeftFace));
    if (leftNet == null)
      return;

    float power01 = power / Math.Max(0.01f, Engine?.MaxPower ?? 1f);
    float maxPressure = SmexValues.AirCompressionRatio * power01;
    float amount = SmexValues.AirBlowerOutputPerSecond * power * dt;

    leftNet.TryProduceGas(
      amount,
      20f,
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: maxPressure,
      sourcePos: Pos.AddCopy(LeftFace)
    );
  }
}

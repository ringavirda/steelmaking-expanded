using System;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Cornish-engine sub-machine: a water pump. The pump is not itself a source of water —
/// the fluid intake is the generator. While powered, the pump looks for a fluid intake on
/// the network below it: if there is one, it makes that intake produce water into the
/// bottom (source) network and transfers the same volume on into the left (water-line)
/// network at a pressure proportional to the engine's inlet steam. With no intake the
/// pump still runs (and demands power) but moves nothing.
/// </summary>
[EntityRegister]
public class BlockEntityEngineFluidPump : BlockEntityEngineSubmachine
{
  protected override void DoWork(float power, float dt)
  {
    if (power <= 0f)
      return;

    var ba = Api.World.BlockAccessor;
    PipeNetwork? bottomNet = ConnectedNetwork(BlockFacing.DOWN);
    PipeNetwork? leftNet = ConnectedNetwork(LeftFace);

    // The intake is the generator; without one the pump runs and does nothing.
    BlockEntityFluidIntake? intake = FindIntake(bottomNet);
    if (intake == null)
      return;

    // Output pressure tracks the driving engine's inlet steam pressure × efficiency, and
    // the volume scales with the engine's power fraction (throttle / tier).
    float power01 = power * 3 / Math.Max(0.01f, Engine?.MaxPower ?? 1f);
    float pressure =
      (Engine?.InletPressure ?? 0f) * PpexValues.SteamEngineEfficiency;
    float amount = PpexValues.PumpWaterPerSecond * power01 * dt;

    // Transfer the standing water already in the bottom (source) network out into the
    // water line first — capped by the output's free capacity — then have the intake
    // refill the bottom network. Refilling last leaves the bottom net holding ~amount of
    // water (and a feed pressure) at broadcast time, so it reads as a water line rather
    // than the empty "Air" gas pool that the shared pipe would otherwise report. Surplus
    // the output cannot hold simply stays in the bottom net up to its capacity.
    float move = Math.Min(amount, OutputFreeCapacity(leftNet));
    float drawn = bottomNet?.TryConsumeLiquid(move, ba) ?? 0f;
    if (drawn > 0f)
      leftNet?.TryProduceLiquid(drawn, 20f, pressure, ba);

    intake.ProduceWater(amount, 20f, ba);
  }

  /// <summary>The first fluid intake on <paramref name="net"/> that can currently draw water, or <c>null</c>.</summary>
  private BlockEntityFluidIntake? FindIntake(PipeNetwork? net)
  {
    if (net == null)
      return null;
    var ba = Api.World.BlockAccessor;
    foreach (var p in net.Nodes)
    {
      if (
        ba.GetBlockEntity(p) is BlockEntityFluidIntake intake
        && intake.CanIntake
      )
        return intake;
    }
    return null;
  }

  /// <summary>Litres of water the output network can still accept.</summary>
  private static float OutputFreeCapacity(PipeNetwork? net) =>
    net == null
      ? 0f
      : net.Nodes.Count * PpexValues.LitresPerPipe - (net.State?.Volume ?? 0f);
}

using System;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
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
  /// <summary>True while the pump has an active intake on its source line and is moving water;
  /// synced to clients to drive the water-drawing loop sound.</summary>
  private bool _drawingWater;

  private ILoadedSound? _waterSound;

  protected override void DoWork(float power, float dt)
  {
    if (power <= 0f)
    {
      SetDrawing(false);
      return;
    }

    var ba = Api.World.BlockAccessor;
    PipeNetwork? bottomNet = ConnectedNetwork(BlockFacing.DOWN);
    PipeNetwork? leftNet = ConnectedNetwork(LeftFace);

    // The intake is the generator; without one the pump runs and does nothing.
    BlockEntityFluidIntake? intake = FindIntake(bottomNet);
    SetDrawing(intake != null);
    if (intake == null)
      return;

    float pressure =
      (Engine?.InletPressure ?? 0f) * PpexValues.SteamEngineEfficiency;
    // Output scales with the engine's absolute mechanical power, so a stronger engine pumps
    // proportionally more (Watt at 0.3 → 5 L/s, Cornish 0.2/0.4/0.8 → 3.3/6.7/13.3 L/s).
    float amount = PpexValues.PumpWaterPerSecond * power * dt;

    float move = Math.Min(amount, OutputFreeCapacity(leftNet));
    float drawn = bottomNet?.TryConsumeLiquid(move, ba) ?? 0f;
    if (drawn > 0f)
      leftNet?.TryProduceLiquid(drawn, 20f, pressure, ba);

    intake.ProduceWater(amount, 20f, ba);
  }

  /// <summary>Updates the synced water-drawing flag, syncing to clients only on change.</summary>
  private void SetDrawing(bool drawing)
  {
    if (drawing == _drawingWater)
      return;
    _drawingWater = drawing;
    MarkDirty();
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

  /// <summary>
  /// Runs a watering trickle loop while the pump is actually drawing water, on top of the
  /// shared piston-stroke sounds — the same loop the manual fluid pump uses.
  /// </summary>
  protected override void OnClientStateTick(float dt)
  {
    if (Api is not ICoreClientAPI)
      return;

    if (_drawingWater)
    {
      _waterSound ??= ExSounds.CreateLoop(
        Api,
        Pos,
        ExSounds.Watering,
        volume: 0.6f,
        range: 16f
      );
      if (_waterSound?.IsPlaying == false)
        _waterSound.Start();
    }
    else if (_waterSound?.IsPlaying == true)
      _waterSound.Stop();
  }

  private void DisposeSounds()
  {
    _waterSound?.Stop();
    _waterSound?.Dispose();
    _waterSound = null;
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetBool("drawingWater", _drawingWater);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _drawingWater = tree.GetBool("drawingWater");
  }

  public override void OnBlockRemoved()
  {
    DisposeSounds();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    DisposeSounds();
    base.OnBlockUnloaded();
  }
}

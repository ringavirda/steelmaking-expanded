using System;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Cornish-engine sub-machine: an air compressor. While powered it injects air into
/// its left network at a pressure of the engine's inlet steam × efficiency; once that
/// air crosses the blast threshold (≥ 2.5 atm) it counts as blast. The mod's only air
/// source.
/// </summary>
[EntityRegister]
public class BlockEntityEngineAirBlower : BlockEntityEngineSubmachine
{
  // Cylinder cycle keyframes (60-frame loop): the piston tops out at frame 15 (intake) and
  // bottoms out at frame 45 (compression). See assets cornish/airblower.json.
  private const int PistonTopFrame = 15;
  private const int PistonBottomFrame = 45;

  /// <summary>World point at the open top of the cylinder. The piston travels on the block's
  /// centre line, so this is rotation-independent — the air is drawn straight down into it.</summary>
  private Vec3d CylinderMouth => new(Pos.X + 0.5, Pos.Y + 0.9, Pos.Z + 0.5);

  /// <summary>
  /// The air blower has its own single-acting cylinder rather than the engine's reciprocating
  /// piston, so it replaces the shared stroke sounds: a bellows wheeze as the piston tops out
  /// (intake) and an iron clang as it bottoms out (compression). On the intake stroke it also
  /// draws a wisp of faint ambient air down into the open top of the cylinder.
  /// </summary>
  protected override void OnCycleStroke(float last, float cur, int total)
  {
    if (PistonCycleSounds.CrossedFrame(last, cur, total, PistonTopFrame))
    {
      ExSounds.PlayLocal(Api.World, Pos, ExSounds.Bellows, 0.6f, 16f);
      ExParticles.AirInhale(Api.World, CylinderMouth, 4);
    }
    if (PistonCycleSounds.CrossedFrame(last, cur, total, PistonBottomFrame))
      ExSounds.PlayLocal(Api.World, Pos, ExSounds.AnvilMergeHit, 0.2f, 16f);
  }

  protected override void DoWork(float power, float dt)
  {
    if (power <= 0f)
      return;
    PipeNetwork? leftNet = ConnectedNetwork(LeftFace);
    if (leftNet == null)
      return;

    float power01 = power * 3 / Math.Max(0.01f, Engine?.MaxPower ?? 1f);
    float maxPressure =
      (Engine?.InletPressure ?? 0f) * PpexValues.SteamEngineEfficiency;
    float amount = SmexValues.AirBlowerOutputPerSecond * power01 * dt;

    leftNet.TryProduceGas(
      amount,
      20f,
      "Air",
      Api.World.BlockAccessor,
      maxOutputPressure: maxPressure
    );
  }
}

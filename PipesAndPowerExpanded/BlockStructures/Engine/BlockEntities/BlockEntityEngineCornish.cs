using System;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Cornish engine — the steel, high-pressure, efficient tier. Its steam control
/// rods (sneak + right-click) set a throttle that shifts the whole operating band:
/// <list type="bullet">
///   <item><b>Underclocked</b> — engages at a lower pressure (down to ~4 atm) for reduced
///   power, so it can limp along on a weak supply.</item>
///   <item><b>Overclocked</b> — engages higher (up to ~8 atm) for boosted power, drawing
///   more steam and pulling network pressure down faster.</item>
/// </list>
/// The over-pressure break threshold rides up with the throttle, so a fully overclocked
/// engine tolerates ~9 atm before bursting. The break itself lives in
/// <see cref="BlockEntityEngineBase"/>.
/// </summary>
[EntityRegister]
public class BlockEntityEngineCornish : BlockEntityEngineBase
{
  // Control-rod throttle (0 = full underclock, 1 = full overclock), in 0.25 steps.
  private float _throttle = 0.5f;

  /// <summary>The current control-rod throttle, 0..1.</summary>
  public float ThrottleSetting => Math.Clamp(_throttle, 0f, 1f);

  /// <summary>Inlet pressure (atm) the engine engages at for the current throttle.</summary>
  public float EngagePressure =>
    GameMath.Lerp(
      PpexValues.CornishEngineUnderclockPressure,
      PpexValues.CornishEngineOverclockPressure,
      ThrottleSetting
    );

  /// <summary>Delivered power for the current throttle (below nominal when underclocked, above when overclocked).</summary>
  public float ThrottlePower =>
    GameMath.Lerp(
      PpexValues.CornishEngineUnderclockPower,
      PpexValues.CornishEngineOverclockPower,
      ThrottleSetting
    );

  protected override float MaxPowerValue => PpexValues.CornishEngineMaxPower;
  protected override float SteamPerPower =>
    PpexValues.CornishEngineSteamPerPower;

  // The break threshold rides above the engage pressure, so it climbs with the throttle.
  protected override float OverPressureThreshold =>
    EngagePressure + PpexValues.CornishEngineOverPressureMargin;

  protected override float ComputePower(float inletPressure)
  {
    // Runs once the inlet meets the throttle's engage pressure; power is set by the throttle.
    if (inletPressure < EngagePressure)
      return 0f;
    return ThrottlePower;
  }

  /// <summary>
  /// Advances the control rods one step, wrapping from full overclock back to full
  /// underclock. Called server-side from the block interaction.
  /// </summary>
  public void CycleThrottle()
  {
    float next = ThrottleSetting + 0.25f;
    if (next > 1.001f)
      next = 0f;
    _throttle = next;
    MarkDirty(true);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("throttle", _throttle);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _throttle = tree.GetFloat("throttle", 0.5f);
  }

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  )
  {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed || IsBroken)
      return;

    dsc.AppendLine(
      Lang.Get(
        "ppex:engine-info-throttle",
        ThrottleSetting * 100f,
        EngagePressure,
        OverPressureThreshold
      )
    );
  }
}

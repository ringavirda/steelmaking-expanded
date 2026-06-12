using System;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Cornish engine — the steel, high-pressure, efficient tier. Its steam control
/// rods (sneak + right-click) set how much steam it admits, which sets its power:
/// <list type="bullet">
///   <item><b>Low</b> — 12 L/s steam, half power.</item>
///   <item><b>Normal</b> — 24 L/s steam, nominal power.</item>
///   <item><b>High</b> — 48 L/s steam, double power.</item>
/// </list>
/// The operating band is fixed (6–8 atm) — the control rods do not move it, so the
/// engine never sits over-pressure just from a throttle change. The break itself lives
/// in <see cref="BlockEntityEngine"/>.
/// </summary>
[EntityRegister]
public class BlockEntityEngineCornish : BlockEntityEngine
{
  // Control-rod setting: 0 = low, 1 = normal, 2 = high.
  private int _throttle = 1;

  /// <summary>The current control-rod setting index, 0..2.</summary>
  public int ThrottleIndex => Math.Clamp(_throttle, 0, 2);

  private static readonly string[] ThrottleKeys = ["low", "normal", "high"];

  /// <summary>Lang key fragment for the current setting.</summary>
  public string ThrottleKey => ThrottleKeys[ThrottleIndex];

  protected override float MaxPowerValue => PpexValues.CornishEngineMaxPower;
  protected override float EngagePressure =>
    PpexValues.CornishEngineEngagePressure;
  protected override float BreakPressure =>
    PpexValues.CornishEngineBreakPressure;

  protected override float RunSteamRate =>
    ThrottleIndex switch
    {
      0 => PpexValues.CornishEngineSteamLow,
      2 => PpexValues.CornishEngineSteamHigh,
      _ => PpexValues.CornishEngineSteamNormal,
    };

  protected override float RunPower =>
    ThrottleIndex switch
    {
      0 => PpexValues.CornishEnginePowerLow,
      2 => PpexValues.CornishEnginePowerHigh,
      _ => PpexValues.CornishEnginePowerNormal,
    };

  protected override float RunWaterOutput =>
    ThrottleIndex switch
    {
      0 => PpexValues.CornishEngineWaterLow,
      2 => PpexValues.CornishEngineWaterHigh,
      _ => PpexValues.CornishEngineWaterNormal,
    };

  /// <summary>Advances the control rods one step, wrapping high → low. Called server-side.</summary>
  public void CycleThrottle()
  {
    _throttle = (ThrottleIndex + 1) % 3;
    MarkDirty(true);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetInt("throttle", _throttle);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    _throttle = tree.GetInt("throttle", 1);
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
        Lang.Get("ppex:engine-throttle-" + ThrottleKey),
        EngagePressure,
        BreakPressure
      )
    );
  }
}

using System;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Cornish engine — the steel, high-pressure, efficient tier. Its steam control
/// rods (wrench right-click to raise, wrench ctrl + right-click to lower) set how much
/// steam it admits, which sets its power:
/// <list type="bullet">
///   <item><b>Low</b> — half steam, half power.</item>
///   <item><b>Normal</b> — nominal steam, nominal power.</item>
///   <item><b>High</b> — double steam, double power.</item>
/// </list>
/// The control rods also raise the operating band with the steam admission: low works on
/// a gentle 4–6 atm, normal on 6–8 atm, high demands a hot 8–9 atm — so throttling up needs
/// a hotter line to engage and breaks sooner once it does. The break itself lives in
/// <see cref="BlockEntityEngine"/>.
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

  // The control rods raise the whole operating band with the steam admission: low runs on a
  // gentle 4–6 atm, normal on 6–8 atm, high demands a hot 8–9 atm. Throttling up therefore
  // needs a hotter line, and throttling down lets the engine work off a softer one.
  protected override float EngagePressure =>
    ThrottleIndex switch
    {
      0 => PpexValues.CornishEngineEngagePressureLow,
      2 => PpexValues.CornishEngineEngagePressureHigh,
      _ => PpexValues.CornishEngineEngagePressureNormal,
    };

  protected override float BreakPressure =>
    ThrottleIndex switch
    {
      0 => PpexValues.CornishEngineBreakPressureLow,
      2 => PpexValues.CornishEngineBreakPressureHigh,
      _ => PpexValues.CornishEngineBreakPressureNormal,
    };

  // Cylinder steam scales with the throttle: double the puff when running high, none at all
  // when throttled down to its gentle low setting.
  protected override int CylinderSteamPuffCount =>
    ThrottleIndex switch
    {
      0 => 0,
      2 => 4,
      _ => 2,
    };

  // Overclocked, the engine works a hotter, harder cycle — its strokes hit louder and the gear
  // train growls lower and meaner. Normal and low settings are left unchanged.
  protected override float SoundVolumeFactor =>
    ThrottleIndex == 2 ? PpexValues.CornishEngineOverclockVolume : 1f;

  protected override float SoundPitchFactor =>
    ThrottleIndex == 2 ? PpexValues.CornishEngineOverclockPitch : 1f;

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

  /// <summary>
  /// Moves the control rods one step in <paramref name="direction"/> (positive raises
  /// toward <c>high</c>, negative lowers toward <c>low</c>), clamped at either end.
  /// Returns <c>true</c> when the setting actually changed (so the caller can give the
  /// "already at max/min" feedback otherwise). Called server-side.
  /// </summary>
  public bool AdjustThrottle(int direction)
  {
    int next = Math.Clamp(ThrottleIndex + Math.Sign(direction), 0, 2);
    if (next == ThrottleIndex)
      return false;
    _throttle = next;
    MarkDirty(true);
    return true;
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

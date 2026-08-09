using System;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Cornish engine - the steel, high-pressure, efficient tier. Its steam control rods (wrench
/// right-click to raise, wrench ctrl+right-click to lower) set how much steam it admits, hence its
/// power: low = half, normal = nominal, high = double. Throttling up also raises the operating band
/// (see the per-setting <see cref="EngagePressure"/>/<see cref="BreakPressure"/>), so it needs a
/// hotter line to engage. The break itself lives in <see cref="BlockEntityEngine"/>.
/// </summary>
[BlockEntityRegister]
public class BlockEntityEngineCornish : BlockEntityEngine {
  // Control-rod setting: 0 = low, 1 = normal, 2 = high.
  private int _throttle = 1;

  /// <summary>The current control-rod setting index, 0..2.</summary>
  public int ThrottleIndex => Math.Clamp(_throttle, 0, 2);

  private static readonly string[] ThrottleKeys = ["low", "normal", "high"];

  /// <summary>Lang key fragment for the current setting.</summary>
  public string ThrottleKey => ThrottleKeys[ThrottleIndex];

  protected override float MaxPowerValue => PpexValues.CornishEngineMaxPower;

  // The control rods raise the operating band with the steam admission, so throttling up needs
  // a hotter line and throttling down works off a softer one.
  protected override float EngagePressure =>
    ThrottleIndex switch {
      0 => PpexValues.CornishEngineEngagePressureLow,
      2 => PpexValues.CornishEngineEngagePressureHigh,
      _ => PpexValues.CornishEngineEngagePressureNormal,
    };

  protected override float BreakPressure =>
    ThrottleIndex switch {
      0 => PpexValues.CornishEngineBreakPressureLow,
      2 => PpexValues.CornishEngineBreakPressureHigh,
      _ => PpexValues.CornishEngineBreakPressureNormal,
    };

  // Cylinder steam scales with throttle: double the puff on high, none on low.
  protected override int CylinderSteamPuffCount =>
    ThrottleIndex switch {
      0 => 0,
      2 => 4,
      _ => 2,
    };

  // Overclocked (high), strokes hit louder and the gear train growls lower; low/normal unchanged.
  protected override float SoundVolumeFactor =>
    ThrottleIndex == 2 ? PpexValues.CornishEngineOverclockVolume : 1f;

  protected override float SoundPitchFactor =>
    ThrottleIndex == 2 ? PpexValues.CornishEngineOverclockPitch : 1f;

  protected override float RunSteamRate =>
    ThrottleIndex switch {
      0 => PpexValues.CornishEngineSteamLow,
      2 => PpexValues.CornishEngineSteamHigh,
      _ => PpexValues.CornishEngineSteamNormal,
    };

  protected override float RunPower =>
    ThrottleIndex switch {
      0 => PpexValues.CornishEnginePowerLow,
      2 => PpexValues.CornishEnginePowerHigh,
      _ => PpexValues.CornishEnginePowerNormal,
    };

  protected override float RunWaterOutput =>
    ThrottleIndex switch {
      0 => PpexValues.CornishEngineWaterLow,
      2 => PpexValues.CornishEngineWaterHigh,
      _ => PpexValues.CornishEngineWaterNormal,
    };

  /// <summary>
  /// Moves the control rods one step in <paramref name="direction"/> (positive = toward high),
  /// clamped. Returns <c>true</c> when the setting changed. Server-side.
  /// </summary>
  public bool AdjustThrottle(int direction) {
    int next = Math.Clamp(ThrottleIndex + Math.Sign(direction), 0, 2);
    if (next == ThrottleIndex)
      return false;
    _throttle = next;
    MarkDirty(true);
    return true;
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetInt("throttle", _throttle);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    _throttle = tree.GetInt("throttle", 1);
  }

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  ) {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed || IsBroken)
      return;

    dsc.AppendLine(
      Lang.Get(
        "ppex:engine-info-throttle",
        Lang.Get("ppex:engine-throttle-" + ThrottleKey),
        ExMeasure.PressureRange(EngagePressure, BreakPressure)
      )
    );
  }
}

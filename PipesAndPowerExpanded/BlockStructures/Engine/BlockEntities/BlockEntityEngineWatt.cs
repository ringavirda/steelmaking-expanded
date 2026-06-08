using System;
using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Watt engine — the cheap, iron-buildable, low-pressure tier. Runs on the
/// pressures a Cornish boiler supplies but is thirsty (fills the whole cylinder) and
/// less efficient, and has no control rods: its power follows a fixed linear curve
/// between its minimum and rated pressure. All behavior lives in
/// <see cref="BlockEntityEngineBase"/>.
/// </summary>
[EntityRegister]
public class BlockEntityEngineWatt : BlockEntityEngineBase
{
  protected override float MaxPowerValue => PpexValues.WattEngineMaxPower;
  protected override float SteamPerPower => PpexValues.WattEngineSteamPerPower;
  protected override float OverPressureThreshold =>
    PpexValues.WattEngineMaxPressure;

  protected override float ComputePower(float inletPressure)
  {
    float min = PpexValues.WattEngineMinPressure;
    if (inletPressure < min)
      return 0f;
    // Power scales across the band; at/above the band top it sits at full power
    // (and the base class's over-pressure timer starts ticking toward a break).
    float span = Math.Max(0.01f, PpexValues.WattEngineMaxPressure - min);
    return Math.Clamp((inletPressure - min) / span, 0f, 1f) * MaxPowerValue;
  }
}

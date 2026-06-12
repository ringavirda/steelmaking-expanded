using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Watt engine — the cheap, iron-buildable, low-pressure tier. Runs on the
/// pressures a Cornish boiler supplies (2–4 atm band) but is thirsty: it draws a fixed
/// 30 L/s of steam while running and has no control rods. All behavior lives in
/// <see cref="BlockEntityEngine"/>.
/// </summary>
[EntityRegister]
public class BlockEntityEngineWatt : BlockEntityEngine
{
  protected override float MaxPowerValue => PpexValues.WattEngineMaxPower;
  protected override float EngagePressure =>
    PpexValues.WattEngineEngagePressure;
  protected override float BreakPressure => PpexValues.WattEngineBreakPressure;
  protected override float RunSteamRate => PpexValues.WattEngineSteamRate;
  protected override float RunPower => PpexValues.WattEngineMaxPower;
  protected override float RunWaterOutput => PpexValues.WattEngineWaterRate;
}

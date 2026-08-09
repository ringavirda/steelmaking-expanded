using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// The Watt engine - the cheap, iron-buildable, low-pressure tier. Runs on the
/// pressures a Cornish boiler supplies (2-4 atm band) but is thirsty: it draws a fixed
/// 30 L/s of steam while running and has no control rods. All behavior lives in
/// <see cref="BlockEntityEngine"/>.
/// </summary>
[BlockEntityRegister]
public class BlockEntityEngineWatt : BlockEntityEngine {
  protected override float MaxPowerValue => PpexValues.WattEngineMaxPower;
  protected override float EngagePressure =>
    PpexValues.WattEngineEngagePressure;
  protected override float BreakPressure => PpexValues.WattEngineBreakPressure;
  protected override float RunSteamRate => PpexValues.WattEngineSteamRate;
  protected override float RunPower => PpexValues.WattEngineMaxPower;
  protected override float RunWaterOutput => PpexValues.WattEngineWaterRate;

  public override void GetBlockInfo(
    IPlayer forPlayer,
    System.Text.StringBuilder dsc
  ) {
    base.GetBlockInfo(forPlayer, dsc);
    if (!IsConstructed || IsBroken)
      return;

    // Show the fixed operating band through ExMeasure (like the Cornish engine), so it converts
    // with the player's measurement preference instead of reading a hardcoded "2-4 atm".
    dsc.AppendLine(
      Lang.Get(
        "ppex:engine-info-band",
        ExMeasure.PressureRange(EngagePressure, BreakPressure)
      )
    );
  }
}

using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;

/// <summary>
/// The Lancashire boiler — the steel, high-pressure tier (up to 15 atm). All behavior
/// lives in <see cref="BlockEntityBoilerBase"/>; this only supplies the variant stats.
/// </summary>
[EntityRegister]
public class BlockEntityBoilerLancashire : BlockEntityBoilerBase
{
  protected override float WaterCapacity => PpexValues.BoilerWaterCapacity;
  protected override float MaxInternalSteam =>
    PpexValues.BoilerMaxInternalSteam;
  protected override float SteamPerSecond => PpexValues.BoilerSteamPerSecond;
  protected override float MaxOutputPressure =>
    PpexValues.BoilerMaxOutputPressure;
  protected override float MaxTemperature => PpexValues.BoilerMaxTemperature;
}

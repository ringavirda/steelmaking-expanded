using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;

/// <summary>
/// The Lancashire boiler — the steel, high-pressure tier (up to 15 atm). All behavior
/// lives in <see cref="BlockEntityBoiler"/>; this only supplies the variant stats.
/// </summary>
[EntityRegister]
public class BlockEntityBoilerLancashire : BlockEntityBoiler
{
  protected override float Capacity => PpexValues.BoilerCapacity;
  protected override float MinBoilWater => PpexValues.BoilerMinBoilWater;
  protected override float MaxBoilWater => PpexValues.BoilerMaxBoilWater;
  protected override float SteamPerSecond => PpexValues.BoilerSteamPerSecond;
  protected override float MaxOutputPressure =>
    PpexValues.BoilerMaxOutputPressure;
  protected override int ExplosionRadius => PpexValues.BoilerExplosionRadius;
}

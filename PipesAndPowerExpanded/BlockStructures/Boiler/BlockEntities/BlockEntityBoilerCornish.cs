using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;

/// <summary>
/// The Cornish boiler — the cheap, iron-buildable entry tier. Smaller water/steam
/// capacity, slower conversion, and capped at 5 atm so it cannot drive a Cornish
/// engine. All behavior lives in <see cref="BlockEntityBoiler"/>; this only
/// supplies the variant stats (the shorter water-surface footprint comes from the
/// block's <c>waterRendererBox</c> attribute in the JSON).
/// </summary>
[EntityRegister]
public class BlockEntityBoilerCornish : BlockEntityBoiler
{
  protected override float Capacity => PpexValues.CornishBoilerCapacity;
  protected override float MinBoilWater => PpexValues.CornishBoilerMinBoilWater;
  protected override float MaxBoilWater => PpexValues.CornishBoilerMaxBoilWater;
  protected override float SteamPerSecond =>
    PpexValues.CornishBoilerSteamPerSecond;
  protected override float MaxOutputPressure =>
    PpexValues.CornishBoilerMaxOutputPressure;
  protected override int ExplosionRadius =>
    PpexValues.CornishBoilerExplosionRadius;
}

using Vintagestory.API.Datastructures;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Boiler placement/render geometry, surfaced through the generated attribute accessors so the
/// abstract <see cref="BlockBoiler"/> base (and the shared <c>BlockEntityBoiler</c>) can resolve its
/// offsets and the water-surface box without reading attributes by name. The concrete boilers
/// (Lancashire/Cornish) satisfy it for free via their generated members; the base casts to it.
/// </summary>
public interface IBoilerGeometry {
  JsonObject? FuelOffset { get; }
  JsonObject? ExhaustOutletOffset { get; }
  JsonObject? LidOffset { get; }
  JsonObject? SteamConnectorOffset { get; }
  JsonObject? LightSampleOffset { get; }
  JsonObject? ExplosionCenterOffset { get; }
  JsonObject? WaterRendererBox { get; }
}

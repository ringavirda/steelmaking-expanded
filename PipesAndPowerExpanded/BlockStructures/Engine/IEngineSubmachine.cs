namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// A machine driven by a Cornish engine. The engine reads <see cref="PowerDemand"/> each tick
/// to consume only the steam its sub-machine can actually use. Implement this (or, for the easy
/// path, extend <c>BlockEntityCornishSubmachine</c>, which handles engine discovery, animation
/// mirroring and ticking) to add a new sub-machine from another mod.
/// </summary>
public interface IEngineSubmachine
{
  /// <summary>Fraction of available engine power (0..1) this sub-machine can use this tick.</summary>
  float PowerDemand { get; }
}

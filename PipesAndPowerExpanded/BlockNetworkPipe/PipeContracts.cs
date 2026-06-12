using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe;

/// <summary>
/// Required methods for a block to be able to generate gas for the network.
/// </summary>
public interface IPipeProducer
{
  /// <summary>Injects <paramref name="volume"/> litres of <paramref name="gasType"/> at <paramref name="temperature"/> °C into the network. Returns <c>true</c> if any was accepted.</summary>
  bool TryProduce(
    float volume,
    float temperature,
    string gasType = "Air",
    float maxOutputPressure = 1.0f,
    bool bypassLeakCap = false
  );
}

/// <summary>
/// Multiblock structure can use the state of this gas block as an input.
/// </summary>
public interface IPipeConsumer
{
  /// <summary>Withdraws up to <paramref name="requestedVolume"/> litres from the network. Returns the volume actually consumed.</summary>
  float TryConsume(float requestedVolume);

  /// <summary>
  /// Returns current gas or liquid pressure in the network. Liquid is prioritized.
  /// </summary>
  float CurrentNetworkPressure { get; }

  /// <summary>
  /// Returns current volume of gas or liquid in the network. Liquid is prioritized.
  /// </summary>
  float CurrentNetworkVolume { get; }
}

/// <summary>
/// Doesn't break the network.
/// Prioritizes consumers connected from one side based on orientation.
/// </summary>
public interface IPressureValve
{
  /// <summary>
  /// Returns the face that should receive priority gas flow.
  /// </summary>
  BlockFacing? GetPriorityFace();
}

/// <summary>
/// Marker for a non-network block that seals against a pipe end: a passthrough pointing
/// into it is not treated as an open leak. Implemented by host-mod blocks (e.g. a heat
/// sink that a pipe feeds into) so the pipe code needs no reference to those blocks.
/// </summary>
public interface IPipeSealingBlock { }

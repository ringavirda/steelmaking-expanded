using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas;

/// <summary>
/// Holds the current state of the entire gas network.
/// </summary>
public class GasNetworkState
{
  /// <summary>Gas currently held by the network, in m³.</summary>
  public float CurrentVolume { get; set; }

  /// <summary>Maximum gas the network can hold (1 m³ per pipe node).</summary>
  public float MaxVolume { get; set; }

  /// <summary>Temperature (°C) of the gas as injected by the producing source.</summary>
  public float SourceTemperature { get; set; }

  /// <summary>Current gas kind: "Air", "Blast", or "Exhaust".</summary>
  public string GasType { get; set; } = "Air";

  /// <summary>Number of open-ended connectors (leaks) on the network.</summary>
  public int OpeningsCount { get; set; } = 0;

  /// <summary>
  /// Gas throughput in m³/s — the volume that moved through the network over the
  /// last second (max of produced and consumed). Non-zero even when production and
  /// consumption cancel to ~0 net volume, so a busy pass-through line doesn't read
  /// as empty. Computed once per second by <c>GasNetwork.OnTick</c>.
  /// </summary>
  public float FlowRate { get; set; } = 0f;

  /// <summary>Whether the network has any open-ended connectors.</summary>
  public bool IsLeaking => OpeningsCount > 0;

  /// <summary>
  /// Returns the higher-priority gas of two types when networks mix
  /// (Exhaust &gt; Blast &gt; Air).
  /// </summary>
  public static string GetHigherPriorityGas(string type1, string type2)
  {
    if (type1 == "Exhaust" || type2 == "Exhaust")
      return "Exhaust";
    if (type1 == "Blast" || type2 == "Blast")
      return "Blast";
    return "Air";
  }
}

/// <summary>
/// Required methods for block to be able to generate gas for the network.
/// </summary>
public interface IGasProducer
{
  /// <summary>Injects <paramref name="volume"/> m³ of <paramref name="gasType"/> at <paramref name="temperature"/> °C into the network. Returns <c>true</c> if any was accepted.</summary>
  bool TryProduceGas(float volume, float temperature, string gasType = "Air");
}

/// <summary>
/// Multiblock structure can use the state of this gas block as an input.
/// </summary>
public interface IGasConsumer
{
  /// <summary>Withdraws up to <paramref name="requestedVolume"/> m³ from the network. Returns the volume actually consumed.</summary>
  float TryConsumeGas(float requestedVolume);
}

/// <summary>
/// Doesn't break the network
/// Prioritizes consumers connected from one side based on orientation
/// </summary>
public interface IGasPressureValve
{
  /// <summary>
  /// Returns the face that should receive priority gas flow.
  /// </summary>
  BlockFacing? GetPriorityFace();
}

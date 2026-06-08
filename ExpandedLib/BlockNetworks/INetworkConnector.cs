using Vintagestory.API.MathTools;

namespace ExpandedLib.BlockNetworks;

/// <summary>
/// Block-level interface for anything a network pipe can connect to. Implemented by
/// <see cref="BlockNetworkNode"/> (the pipes/canals themselves) and by structure
/// blocks that expose a fixed network port on certain faces without being a full
/// network node (e.g. the lancashire boiler's water intake, the cornish engine's
/// steam intake / water out). Such ports are valid connection targets for pipes but
/// are not themselves added to the network graph.
/// </summary>
public interface INetworkConnector
{
  /// <summary>Network type this connector belongs to, e.g. "gas", "molten", "pipe".</summary>
  string NetworkType { get; }

  /// <summary>True when this block exposes a network connector on <paramref name="face"/>.</summary>
  bool HasConnectorAt(BlockFacing face);
}

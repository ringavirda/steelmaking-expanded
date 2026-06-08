using Vintagestory.API.MathTools;

namespace ExpandedLib.BlockNetworks;

/// <summary>
/// Base interface for all block entities that participate in a block network
/// (gas pipes, molten canals, …).
/// </summary>
public interface INetworkNode
{
  /// <summary>
  /// The first letter of each direction that has a network connector at this block
  /// (e.g. "ns" for north + south).  May be <c>null</c> while the block is loading.
  /// </summary>
  string? Orientation { get; }

  /// <summary>
  /// All orientation strings valid at this position (used for wrench cycling).
  /// </summary>
  string[] PossibleOrientations { get; }

  /// <summary>Network type identifier, e.g. "gas" or "molten".</summary>
  string NetworkType { get; }

  /// <summary>Returns <c>true</c> when this block has a connector on <paramref name="face"/>.</summary>
  bool HasConnectorAt(BlockFacing face);

  /// <summary>
  /// Called by the network tick with the set of connector faces that have no
  /// valid neighbour (open ends / leaks).  Default: no-op.
  /// </summary>
  void OnOpenConnectorsChanged(BlockFacing[] openFaces);

  /// <summary>
  /// Receives the latest network state so clients can update their display.
  /// </summary>
  void OnNetworkUpdate(object? state);
}

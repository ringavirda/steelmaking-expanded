using Vintagestory.API.Common;
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

  /// <summary>
  /// Position-aware network type — defaults to <see cref="NetworkType"/>. Per-cell connectors
  /// (e.g. a structure filler that exposes a port on one specific footprint cell) override this
  /// to report a network only on the port cell and stay inert elsewhere.
  /// </summary>
  string NetworkTypeAt(IBlockAccessor world, BlockPos pos) => NetworkType;

  /// <summary>
  /// Position-aware connector test — defaults to <see cref="HasConnectorAt(BlockFacing)"/>.
  /// Per-cell connectors read the block entity at <paramref name="pos"/> to answer for that
  /// cell only.
  /// </summary>
  bool HasConnectorAt(IBlockAccessor world, BlockPos pos, BlockFacing face) =>
    HasConnectorAt(face);
}

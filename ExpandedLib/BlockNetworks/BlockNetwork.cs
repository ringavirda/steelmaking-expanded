using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.BlockNetworks;

/// <summary>
/// Abstract base class for all live block-network instances.
/// Each concrete subclass (e.g. <c>GasNetwork</c>, <c>MoltenNetwork</c>) owns its
/// typed state and implements all type-specific network operations — producers,
/// consumers, merge/split/tick.
/// <para>
/// The <see cref="BlockNetworkModSystem"/> only performs graph-level work
/// (BFS add/remove/rebuild); everything else lives here.
/// </para>
/// </summary>
public abstract class BlockNetwork(BlockNetworkModSystem system)
{
  /// <summary>Stable identity for this network instance.</summary>
  public Guid Id { get; } = Guid.NewGuid();

  /// <summary>Identifies the network type, e.g. "gas" or "molten".</summary>
  public abstract string NetworkType { get; }

  /// <summary>Every world position that belongs to this network.</summary>
  public HashSet<BlockPos> Nodes { get; } = [];

  /// <summary>
  /// Authoritative root position for root-anchored networks.  <c>null</c>~
  /// networks and rootless fragments.
  /// </summary>
  public BlockPos? RootPos { get; set; }

  /// <summary>The current network state object (typed by the concrete subclass).</summary>
  public object? State { get; protected set; }

  /// <summary>The network manager that owns this instance.</summary>
  public BlockNetworkModSystem? NetworkSystem { get; set; } = system;

  #region State persistence

  /// <summary>
  /// Injects <paramref name="state"/> directly into this network.
  /// Called by <see cref="BlockEntityNetworkNode"/> during world load to restore
  /// persisted state before the first tick.  Override to cast to the concrete
  /// state type.
  /// </summary>
  public virtual void RestoreState(object? state)
  {
    State = state;
  }

  #endregion

  #region Broadcasting

  /// <summary>
  /// Sends the current typed state to every <see cref="INetworkNode"/> block
  /// entity in this network so clients can update their display.
  /// </summary>
  public void BroadcastUpdate(IBlockAccessor blockAccessor)
  {
    OnBeforeBroadcast(blockAccessor);
    object? payload = GetStatePayload();
    foreach (var pos in Nodes)
    {
      if (blockAccessor.GetBlockEntity(pos) is INetworkNode receiver)
        receiver.OnNetworkUpdate(payload);
    }
  }

  /// <summary>Called by <see cref="BroadcastUpdate"/> before payload is collected and dispatched. Override to update derived state (e.g. recalculate capacity).</summary>
  protected virtual void OnBeforeBroadcast(IBlockAccessor blockAccessor) { }

  /// <summary>Returns the typed state object sent to nodes during a broadcast.</summary>
  protected virtual object? GetStatePayload() => State;

  #endregion

  #region Lifecycle callbacks

  /// <summary>
  /// Returns <c>false</c> to veto a graph-level merge of two adjacent networks
  /// of the same type.  Default: always allow.
  /// </summary>
  public virtual bool CanMerge(BlockNetwork other, IBlockAccessor world) =>
    true;

  /// <summary>
  /// Called when <paramref name="other"/> merges into <c>this</c> network.
  /// Implementations combine state (e.g. weighted-average temperature, total volume).
  /// </summary>
  public abstract void OnMerge(BlockNetwork other, IBlockAccessor world);

  /// <summary>
  /// Called on a freshly-created fragment after network fracture.
  /// <c>this</c> is the new fragment; <paramref name="original"/> is the network
  /// that fractured.  Implementations distribute a proportional share of state.
  /// </summary>
  public abstract void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  );

  /// <summary>Called once per server tick for each live network of this type.</summary>
  public abstract void OnTick(
    IBlockAccessor world,
    float dt,
    BlockNetworkModSystem manager
  );

  /// <summary>
  /// Transfers persistent state from <paramref name="source"/> into <c>this</c>
  /// instance.  Called by <see cref="BlockNetworkModSystem.RebuildFromRoot"/> to
  /// preserve fill/temperature across structural rebuilds.
  /// Default implementation is a no-op; override to copy typed state fields.
  /// </summary>
  public virtual void InheritStateFrom(BlockNetwork source) { }

  /// <summary>
  /// Called by the <see cref="BlockNetworkModSystem"/> after this network's
  /// <see cref="Nodes"/> set changed (node added/removed, networks merged, fragment
  /// built, rebuild). Override to drop caches derived from the node set (e.g. the
  /// pipe network's weakest-pipe burst rating) instead of recomputing them per call.
  /// Default: no-op.
  /// </summary>
  public virtual void OnTopologyChanged() { }

  #endregion
}

using System;
using System.Collections.Generic;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.BlockNetworks;

/// <summary>
/// Graph manager for all block networks.  Handles node add/remove, BFS fracture
/// detection, and per-tick dispatch.  All type-specific state and logic live in
/// the concrete <see cref="BlockNetwork"/> subclasses.
/// </summary>
public class BlockNetworkModSystem : ModSystem
{
  #region Graph storage
  private readonly Dictionary<Guid, BlockNetwork> _networks = [];
  private readonly Dictionary<BlockPos, Guid> _posToNetwork = [];
  private readonly Dictionary<string, Func<BlockNetwork>> _factories = [];

  /// <summary>
  /// Registers a factory that creates a new typed network instance for the
  /// given <paramref name="networkType"/> (e.g. "gas", "molten").
  /// Call once during <c>ModSystem.Start</c>.
  /// </summary>
  public void RegisterNetworkType(
    string networkType,
    Func<BlockNetwork> factory
  ) => _factories[networkType] = factory;

  /// <summary>
  /// Server world accessor, available to network instances during their tick (e.g.
  /// to break a burst/melted node and drop its items). <c>null</c> on the client.
  /// </summary>
  public IServerWorldAccessor? ServerWorld { get; private set; }

  public override void StartServerSide(ICoreServerAPI api)
  {
    ServerWorld = api.World;
    api.Event.RegisterGameTickListener(
      dt => OnServerTick(api.World.BlockAccessor, dt),
      1000
    );
  }

  /// <summary>Returns the network that owns <paramref name="pos"/>, or <c>null</c>.</summary>
  public BlockNetwork? GetNetworkAt(BlockPos pos) =>
    _posToNetwork.TryGetValue(pos, out Guid id)
    && _networks.TryGetValue(id, out var net)
      ? net
      : null;

  /// <summary>
  /// Returns the network in the cell adjacent to <paramref name="connectorPos"/> across
  /// <paramref name="connectorFace"/>, but only when the block occupying that cell actually
  /// exposes a network connector back toward <paramref name="connectorPos"/> (on
  /// <paramref name="connectorFace"/>'s opposite face).
  /// <para>
  /// This is the reciprocal-connection test a fixed machine port must apply before it draws
  /// gas / liquid from — or feeds — a pipe run. A pipe that merely occupies the adjacent cell
  /// without a connector facing the port is <em>not</em> plumbed into it (its orientation
  /// points elsewhere), so the port must not operate on its network. Returns <c>null</c> when
  /// there is no reciprocating connector, or no network in that cell.
  /// </para>
  /// </summary>
  public BlockNetwork? GetConnectedNetworkAcross(
    IBlockAccessor world,
    BlockPos connectorPos,
    BlockFacing connectorFace
  )
  {
    BlockPos neighbourPos = connectorPos.AddCopy(connectorFace);
    return
      world.GetBlock(neighbourPos) is INetworkConnector neighbour
      && neighbour.HasConnectorAt(world, neighbourPos, connectorFace.Opposite)
      ? GetNetworkAt(neighbourPos)
      : null;
  }

  private BlockNetwork CreateNetwork(string networkType)
  {
    if (_factories.TryGetValue(networkType, out var factory))
      return factory();
    throw new InvalidOperationException(
      $"No factory registered for network type '{networkType}'. "
        + $"Call BlockNetworkModSystem.RegisterNetworkType before using this network type."
    );
  }
  #endregion

  #region Graph node manipulation
  /// <summary>
  /// Adds <paramref name="pos"/> to the network graph, merging adjacent networks
  /// of the same type as needed.
  /// </summary>
  /// <param name="broadcast">
  /// When <c>true</c> (default), immediately broadcasts state so clients reflect
  /// the new connectivity.  Pass <c>false</c> during batch operations.
  /// </param>
  public virtual void AddNode(
    IBlockAccessor world,
    BlockPos pos,
    string networkType,
    bool broadcast = true
  )
  {
    var connectedNeighbors = GetConnectedNeighbors(world, pos, networkType)
      .ToList();

    var adjacentNetworks = connectedNeighbors
      .Where(_posToNetwork.ContainsKey)
      .Select(p => _networks[_posToNetwork[p]])
      .Distinct()
      .ToList();

    if (adjacentNetworks.Count == 0)
    {
      // Isolated new node — standalone network, no broadcast needed.
      var net = CreateNetwork(networkType);
      net.Nodes.Add(pos);
      _networks[net.Id] = net;
      _posToNetwork[pos] = net.Id;
      net.OnTopologyChanged();
    }
    else
    {
      // Join the first adjacent network and merge any others into it.
      var primaryNet = adjacentNetworks[0];
      primaryNet.Nodes.Add(pos);
      _posToNetwork[pos] = primaryNet.Id;

      for (int i = 1; i < adjacentNetworks.Count; i++)
      {
        var netToMerge = adjacentNetworks[i];
        if (!primaryNet.CanMerge(netToMerge, world))
          continue;

        foreach (var nPos in netToMerge.Nodes)
        {
          primaryNet.Nodes.Add(nPos);
          _posToNetwork[nPos] = primaryNet.Id;
        }

        primaryNet.OnMerge(netToMerge, world);
        _networks.Remove(netToMerge.Id);
      }

      primaryNet.OnTopologyChanged();
      if (broadcast)
        primaryNet.BroadcastUpdate(world);
    }
  }

  /// <summary>
  /// Removes <paramref name="pos"/> from the network graph, running BFS fracture
  /// detection and splitting the network if it disconnects.
  /// </summary>
  /// <param name="broadcast">
  /// When <c>true</c> (default), broadcasts the updated state to all surviving
  /// fragment nodes.
  /// </param>
  public virtual void RemoveNode(
    IBlockAccessor world,
    BlockPos pos,
    bool broadcast = true
  )
  {
    if (!_posToNetwork.TryGetValue(pos, out Guid netId))
      return;
    if (!_networks.TryGetValue(netId, out BlockNetwork? network))
      return;

    network.Nodes.Remove(pos);
    _posToNetwork.Remove(pos);

    if (network.Nodes.Count == 0)
    {
      _networks.Remove(netId);
      return;
    }

    // BFS to detect fractures: are all remaining nodes still reachable?
    var startNode = network.Nodes.First();
    var visited = new HashSet<BlockPos> { startNode };
    var queue = new Queue<BlockPos>();
    queue.Enqueue(startNode);

    while (queue.Count > 0)
    {
      var curr = queue.Dequeue();
      foreach (
        var adj in GetConnectedNeighbors(world, curr, network.NetworkType)
      )
      {
        if (network.Nodes.Contains(adj) && visited.Add(adj))
          queue.Enqueue(adj);
      }
    }

    if (visited.Count < network.Nodes.Count)
    {
      // Network fractured — rebuild each connected component as its own network.
      var unassigned = new HashSet<BlockPos>(network.Nodes);
      _networks.Remove(netId);

      while (unassigned.Count > 0)
      {
        var newStart = unassigned.First();
        var newNet = CreateNetwork(network.NetworkType);
        _networks[newNet.Id] = newNet;

        var bfsQueue = new Queue<BlockPos>();
        bfsQueue.Enqueue(newStart);
        unassigned.Remove(newStart);
        newNet.Nodes.Add(newStart);
        _posToNetwork[newStart] = newNet.Id;

        while (bfsQueue.Count > 0)
        {
          var curr = bfsQueue.Dequeue();
          foreach (
            var adj in GetConnectedNeighbors(world, curr, network.NetworkType)
          )
          {
            if (unassigned.Contains(adj))
            {
              unassigned.Remove(adj);
              newNet.Nodes.Add(adj);
              _posToNetwork[adj] = newNet.Id;
              bfsQueue.Enqueue(adj);
            }
          }
        }

        // Let the fragment inherit its proportional share of the original state.
        newNet.OnSplitFragment(network, world);
        newNet.OnTopologyChanged();

        if (broadcast)
          newNet.BroadcastUpdate(world);
      }
    }
    else
    {
      // No fracture — network is still fully connected.
      network.OnTopologyChanged();
      if (broadcast)
        network.BroadcastUpdate(world);
    }
  }
  #endregion

  /// <summary>
  /// Completely rebuilds the network rooted at <paramref name="rootPos"/> via BFS,
  /// replacing all existing network entries that overlap with the reachable subgraph.
  /// Preserves state from the old root network so temperature/fill survive rebuilds.
  /// </summary>
  public BlockNetwork? RebuildFromRoot(
    IBlockAccessor world,
    BlockPos rootPos,
    string networkType,
    bool broadcast = true
  )
  {
    if (world.GetBlock(rootPos) is not BlockNetworkNode)
      return null;

    // BFS-discover all reachable positions.
    var reachable = new HashSet<BlockPos>();
    var bfsQueue = new Queue<BlockPos>();
    reachable.Add(rootPos);
    bfsQueue.Enqueue(rootPos);

    while (bfsQueue.Count > 0)
    {
      var curr = bfsQueue.Dequeue();
      foreach (var neighbor in GetConnectedNeighbors(world, curr, networkType))
      {
        if (reachable.Add(neighbor))
          bfsQueue.Enqueue(neighbor);
      }
    }

    // Collect old network IDs that overlap with the reachable set.
    var oldNetIds = new HashSet<Guid>();
    foreach (var pos in reachable)
    {
      if (_posToNetwork.TryGetValue(pos, out Guid id))
        oldNetIds.Add(id);
    }

    // Preserve state of the current root network (temperature, fill level, …).
    _posToNetwork.TryGetValue(rootPos, out Guid rootOldId);
    BlockNetwork? rootOldNet =
      rootOldId != default && _networks.TryGetValue(rootOldId, out var ron)
        ? ron
        : null;

    // Tear down all overlapping old networks.
    foreach (var id in oldNetIds)
    {
      if (_networks.TryGetValue(id, out var oldNet))
      {
        foreach (var p in oldNet.Nodes)
          _posToNetwork.Remove(p);
        _networks.Remove(id);
      }
    }

    // Build the new root-anchored network.
    var newNet = CreateNetwork(networkType);
    newNet.RootPos = rootPos.Copy();

    if (rootOldNet != null)
      newNet.InheritStateFrom(rootOldNet);

    foreach (var pos in reachable)
    {
      newNet.Nodes.Add(pos);
      _posToNetwork[pos] = newNet.Id;
    }

    _networks[newNet.Id] = newNet;
    newNet.OnTopologyChanged();

    if (broadcast)
      newNet.BroadcastUpdate(world);

    return newNet;
  }

  #region Tick
  /// <summary>Called once per second; dispatches <see cref="BlockNetwork.OnTick"/> for every live network.</summary>
  private void OnServerTick(IBlockAccessor blockAccessor, float dt)
  {
    foreach (var network in _networks.Values.ToList())
      network.OnTick(blockAccessor, dt, this);
  }
  #endregion

  #region Public utilities
  /// <summary>
  /// Returns the connector faces on <paramref name="pos"/> that have no valid
  /// network neighbour (open ends / leaks).
  /// </summary>
  public BlockFacing[] GetOpenConnectorFaces(
    IBlockAccessor world,
    BlockPos pos,
    BlockNetworkNode node
  )
  {
    var open = new List<BlockFacing>();
    foreach (var face in BlockFacing.ALLFACES)
    {
      if (!node.HasConnectorAt(face))
        continue;

      BlockPos nPos = pos.AddCopy(face);
      Block nBlock = world.GetBlock(nPos);

      bool connected =
        IsValidNetworkNeighbour(world, node, nBlock, nPos, face)
        || node.IsValidNonNetworkConnection(nBlock, face);
      if (!connected)
        open.Add(face);
    }
    return open.Count == 0 ? [] : open.ToArray();
  }

  /// <summary>
  /// Returns all positions that are graph-connected to <paramref name="pos"/>
  /// (matching connector on the touching face, same network type, not broken).
  /// </summary>
  public IEnumerable<BlockPos> GetConnectedNeighbors(
    IBlockAccessor world,
    BlockPos pos,
    string networkType
  )
  {
    if (world.GetBlock(pos) is not BlockNetworkNode node)
      yield break;

    if (node.IsNetworkEndPoint)
      yield break;

    if (
      world.GetBlockEntity(pos) is BlockEntityNetworkNode be
      && be.IsConnectionBroken()
    )
      yield break;

    foreach (var face in BlockFacing.ALLFACES)
    {
      if (node.HasConnectorAt(face))
      {
        BlockPos neighborPos = pos.AddCopy(face);
        if (
          IsValidNetworkNeighbour(
            world,
            node,
            world.GetBlock(neighborPos),
            neighborPos,
            face
          )
        )
          yield return neighborPos;
      }
    }
  }

  private bool IsValidNetworkNeighbour(
    IBlockAccessor world,
    BlockNetworkNode sourceNode,
    Block neighbourBlock,
    BlockPos neighbourPos,
    BlockFacing facing
  )
  {
    if (
      neighbourBlock is not INetworkConnector neighbourConn
      || neighbourConn.NetworkTypeAt(world, neighbourPos)
        != sourceNode.NetworkType
      || !neighbourConn.HasConnectorAt(world, neighbourPos, facing.Opposite)
    )
      return false;

    // Full network nodes carry extra gating (endpoints, severed connections).
    // Fixed structure ports (INetworkConnector that is not a node) have none —
    // they are a valid target but are never added to the graph themselves.
    if (neighbourBlock is BlockNetworkNode neighbourNode)
    {
      if (neighbourNode.IsNetworkEndPoint)
        return false;

      if (
        world.GetBlockEntity(neighbourPos) is BlockEntityNetworkNode neighbourBe
        && neighbourBe.IsConnectionBroken()
      )
        return false;
    }

    return true;
  }

  /// <summary>Maps a single-char side code ("n","s","e","w","u","d") to its <see cref="BlockFacing"/>.</summary>
  public static BlockFacing? SideToFace(string? side) =>
    side switch
    {
      "n" => BlockFacing.NORTH,
      "s" => BlockFacing.SOUTH,
      "e" => BlockFacing.EAST,
      "w" => BlockFacing.WEST,
      "u" => BlockFacing.UP,
      "d" => BlockFacing.DOWN,
      _ => null,
    };

  /// <summary>Returns <c>true</c> when <paramref name="neighbour"/> is a network block of type <paramref name="id"/>.</summary>
  public static bool IsCompatibleNetworkBlock(Block neighbour, string id) =>
    neighbour is INetworkConnector connector && connector.NetworkType == id;

  /// <summary>
  /// Position-aware compatibility — like <see cref="IsCompatibleNetworkBlock"/> but consults
  /// the connector's per-cell network type, so a structure filler that exposes a port on one
  /// footprint cell reads as compatible only on that cell.
  /// </summary>
  public static bool IsCompatibleNetworkBlockAt(
    IBlockAccessor world,
    BlockPos pos,
    Block neighbour,
    string id
  ) =>
    neighbour is INetworkConnector connector
    && connector.NetworkTypeAt(world, pos) == id;

  public override void Dispose()
  {
    _networks.Clear();
    _posToNetwork.Clear();
    base.Dispose();
  }
  #endregion
}

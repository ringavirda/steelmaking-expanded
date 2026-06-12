using System;
using System.Collections.Generic;
using ExpandedLib.BlockNetworks;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockNetworkMolten;

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the molten-canal system. The network no
/// longer pools metal — each canal block is its own cell
/// (<see cref="BlockEntityMoltenCanal"/>) holding its own metal. The network's only
/// job is connectivity plus the per-tick driver that flows metal cell-to-cell
/// (level-equalisation) and runs each cell's cooling/solidification. Because cells
/// own their metal, merge/split need no redistribution.
/// </summary>
public class MoltenNetwork(BlockNetworkModSystem system) : BlockNetwork(system)
{
  public override string NetworkType => "molten";

  /// <summary>Maps a molten metal item to its solid drop ("game:ingot-iron" → "game:metalbit-iron"); non-ingot items drop as themselves.</summary>
  internal static AssetLocation SolidDropLocation(AssetLocation metalItemLoc)
  {
    if (metalItemLoc.Path.StartsWith("ingot-"))
      return new AssetLocation(
        metalItemLoc.Domain,
        "metalbit-" + metalItemLoc.Path[6..]
      );
    return metalItemLoc;
  }

  // Resolved once from the first loaded node's BE; a network instance never moves
  // between worlds, so walking every node per tick to re-find it was wasted work.
  private IWorldAccessor? _world;

  private IWorldAccessor? GetWorld(IBlockAccessor blockAccessor)
  {
    if (_world != null)
      return _world;
    foreach (var pos in Nodes)
    {
      if (
        blockAccessor.GetBlockEntity(pos) is BlockEntity be
        && be.Api?.World != null
      )
        return _world = be.Api.World;
    }
    return null;
  }

  private static int ComparePos(BlockPos a, BlockPos b)
  {
    int c = a.X.CompareTo(b.X);
    if (c != 0)
      return c;
    c = a.Y.CompareTo(b.Y);
    return c != 0 ? c : a.Z.CompareTo(b.Z);
  }

  // Distance-from-start is purely topological, so it only changes when the set
  // of cells (or which cells are starts) changes. Cache the map and recompute it
  // only when a cheap topology signature differs from the cached one, instead of
  // running a BFS every tick.
  private Dictionary<BlockPos, int>? _cachedDistFromStart;
  private (int Count, long PosHash, long StartHash) _cachedTopoSig;

  /// <summary>
  /// Returns the cached distance-from-start map, rebuilding it (via
  /// <see cref="BuildDistanceFromStart"/>) only when the network's topology
  /// signature has changed since the last computation.
  /// </summary>
  private Dictionary<BlockPos, int> GetDistanceFromStart(
    IBlockAccessor blockAccessor,
    List<BlockEntityMoltenCanal> cells
  )
  {
    var sig = ComputeTopologySignature(cells);
    if (_cachedDistFromStart == null || sig != _cachedTopoSig)
    {
      _cachedDistFromStart = BuildDistanceFromStart(blockAccessor, cells);
      _cachedTopoSig = sig;
    }
    return _cachedDistFromStart;
  }

  /// <summary>
  /// Order-independent fingerprint of the cells that drive the distance map: cell
  /// count plus XOR-folded hashes of all cell positions and of the start-cell
  /// positions. Any add, removal, or start↔plain swap changes at least one term.
  /// </summary>
  private static (int, long, long) ComputeTopologySignature(
    List<BlockEntityMoltenCanal> cells
  )
  {
    long posHash = 0;
    long startHash = 0;
    foreach (var c in cells)
    {
      long h = unchecked(
        (long)((uint)c.Pos.GetHashCode() * 0x9E3779B97F4A7C15UL)
      );
      posHash ^= h;
      if (c is BlockEntityMoltenCanalStart)
        startHash ^= h;
    }
    return (cells.Count, posHash, startHash);
  }

  /// <summary>
  /// Multi-source BFS over the canal graph that maps each cell to its hop
  /// distance from the nearest <see cref="BlockEntityMoltenCanalStart"/>. Cells
  /// unreachable from any start (e.g. a startless run) are simply absent.
  /// </summary>
  private Dictionary<BlockPos, int> BuildDistanceFromStart(
    IBlockAccessor blockAccessor,
    List<BlockEntityMoltenCanal> cells
  )
  {
    var dist = new Dictionary<BlockPos, int>(cells.Count);
    var queue = new Queue<BlockPos>();
    foreach (var c in cells)
      if (c is BlockEntityMoltenCanalStart)
      {
        dist[c.Pos] = 0;
        queue.Enqueue(c.Pos);
      }

    while (queue.Count > 0)
    {
      BlockPos cur = queue.Dequeue();
      int next = dist[cur] + 1;
      if (blockAccessor.GetBlockEntity(cur)?.Block is not BlockNetworkNode node)
        continue;

      foreach (var face in BlockFacing.HORIZONTALS)
      {
        if (!node.HasConnectorAt(face))
          continue;
        BlockPos npos = cur.AddCopy(face);
        if (!Nodes.Contains(npos) || dist.ContainsKey(npos))
          continue;
        if (blockAccessor.GetBlockEntity(npos) is not BlockEntityMoltenCanal)
          continue;
        dist[npos] = next;
        queue.Enqueue(npos);
      }
    }
    return dist;
  }

  /// <summary>
  /// Orders cells for the flow pass: greater distance from the start first, so
  /// metal is driven from the farthest cells back toward the start. Position
  /// breaks ties (including cells unreachable from a start, treated as farthest).
  /// </summary>
  private static int CompareFlowOrder(
    BlockEntityMoltenCanal x,
    BlockEntityMoltenCanal y,
    Dictionary<BlockPos, int> dist
  )
  {
    int dx = dist.TryGetValue(x.Pos, out int vx) ? vx : int.MaxValue;
    int dy = dist.TryGetValue(y.Pos, out int vy) ? vy : int.MaxValue;
    int c = dy.CompareTo(dx); // descending distance: farthest processed first
    return c != 0 ? c : ComparePos(x.Pos, y.Pos);
  }

  #region Tick — flow + cooling
  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  )
  {
    var world = GetWorld(blockAccessor);
    if (world == null)
      return;

    var cells = new List<BlockEntityMoltenCanal>(Nodes.Count);
    foreach (var pos in Nodes)
      if (blockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal c)
        cells.Add(c);
    if (cells.Count == 0)
      return;

    // Order cells by graph distance from the start block, farthest first, so
    // metal drains down the run toward the start a wavefront at a time instead
    // of in an arbitrary positional order. The map is cached and only rebuilt
    // when the network topology changes.
    var distFromStart = GetDistanceFromStart(blockAccessor, cells);
    cells.Sort((x, y) => CompareFlowOrder(x, y, distFromStart));
    foreach (var c in cells)
      c.EnsureMetalStack(world);

    int maxFlow = SmexValues.MoltenFlowRate;
    foreach (var a in cells)
    {
      if (a.Sealed || a.Solidified || a.Block is not BlockNetworkNode aNode)
        continue;

      foreach (var face in BlockFacing.HORIZONTALS)
      {
        if (!aNode.HasConnectorAt(face))
          continue;
        BlockPos npos = a.Pos.AddCopy(face);
        if (!Nodes.Contains(npos))
          continue;
        if (blockAccessor.GetBlockEntity(npos) is not BlockEntityMoltenCanal b)
          continue;
        if (b.Sealed || b.Solidified)
          continue;
        // Drive each undirected edge exactly once, from the cell farther from
        // the start (ties broken by position).
        if (CompareFlowOrder(a, b, distFromStart) >= 0)
          continue;

        FlowEdge(a, b, maxFlow, world);
      }
    }

    // Thermal pass: cool / solidify each cell.
    foreach (var c in cells)
      c.UpdateThermal(world);
  }

  /// <summary>Moves metal across one connection toward equal fill ratio, capped at <paramref name="maxFlow"/> units.</summary>
  private static void FlowEdge(
    BlockEntityMoltenCanal aNode,
    BlockEntityMoltenCanal bNode,
    int maxFlow,
    IWorldAccessor world
  )
  {
    var aCap = aNode.MaxUnitCapacity;
    var bCap = bNode.MaxUnitCapacity;
    if (aCap <= 0 || bCap <= 0)
      return;

    var diff = Math.Abs(aNode.CellAmount - bNode.CellAmount);
    if (diff == 0)
      return;

    bool aIsGiver = aNode.CellAmount > bNode.CellAmount;
    BlockEntityMoltenCanal giver = aIsGiver ? aNode : bNode;
    BlockEntityMoltenCanal receiver = aIsGiver ? bNode : aNode;
    if (giver.CellAmount <= 0f)
      return;

    // Different metals sit side by side without mixing.
    if (
      receiver.CellAmount > 0f
      && receiver.CellMetalType != giver.CellMetalType
    )
      return;

    // Flow moves whole units only, and never less than the minimum per tick —
    // except into a drain fitting (pedestal/tap), which must still receive the
    // final sub-minimum dregs so a run can empty completely into its mold/barrel.
    var transfer = diff > maxFlow ? maxFlow : diff;
    if (
      transfer < SmexValues.MoltenMinFlowAmount
      && receiver is not BlockEntityMoltenCanalMoldPedestal
      && receiver is not BlockEntityMoltenCanalTap
    )
      return;

    var accepted = receiver.PushMetalRaw(
      transfer,
      giver.CellMetalType,
      giver.CellTemperature,
      world
    );
    if (accepted > 0f)
      giver.DrainMetal(accepted);
  }
  #endregion

  #region Graph lifecycle (cells own their metal, so these are trivial)
  public override bool CanMerge(BlockNetwork other, IBlockAccessor world) =>
    other is MoltenNetwork;

  public override void OnMerge(BlockNetwork other, IBlockAccessor world) { }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  ) { }
  #endregion
}

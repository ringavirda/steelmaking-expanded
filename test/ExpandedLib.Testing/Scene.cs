using System;
using System.Collections.Generic;
using ExpandedLib.Blocks.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing;

/// <summary>
/// A composition layer over <see cref="TestWorld"/> for <em>integration</em> tests: lay out several
/// blocks, network nodes and machines in one shared world, then advance them together with a single
/// <see cref="Step"/> that mirrors a server tick - every machine's production tick fires, then every
/// network flows and broadcasts. Where <see cref="TestWorld"/> is the raw store + graph, a
/// <see cref="Scene"/> is the test-facing builder that wires a whole setup and reads back its state.
/// <para>
/// Placement is grid-based and additive; call <see cref="Build"/> once after all placements to add the
/// collected nodes to the graph (which merges adjacent runs into networks). Machines (mega-blocks,
/// engines) are placed by mod-specific fixtures that operate on this scene; flat network topology is
/// most readable through <see cref="SceneDiagram"/>.
/// </para>
/// </summary>
public sealed class Scene {
  /// <summary>The underlying in-memory world (store, graph manager, fake API).</summary>
  public TestWorld World { get; } = new();

  private readonly List<(BlockPos pos, string networkType)> _pendingNodes =
    new();
  private bool _built;

  /// <summary>Registers a network factory, exactly as a mod would at startup.</summary>
  public Scene Network(
    string type,
    System.Func<BlockNetworkModSystem, BlockNetwork> factory
  ) {
    World.RegisterNetwork(type, factory);
    return this;
  }

  /// <summary>Places a plain block (no entity, not a network node) - terrain, caps, machine housings.</summary>
  public Scene Block(BlockPos pos, Block block) {
    World.Place(pos, block);
    return this;
  }

  /// <summary>
  /// Places a network-node block (+ its entity) and queues it to join the graph on <see cref="Build"/>.
  /// The entity is linked to the world API so it can resolve its network and schedule ticks.
  /// </summary>
  public Scene Node(
    BlockPos pos,
    Block block,
    BlockEntity be,
    string networkType
  ) {
    World.Place(pos, block, be);
    World.Attach(be);
    _pendingNodes.Add((pos, networkType));
    return this;
  }

  /// <summary>
  /// Places a machine entity (a non-node block that reads/feeds adjacent networks, e.g. a boiler or
  /// engine) and runs its real <see cref="BlockEntity.Initialize"/> so it schedules its production
  /// tick. Fixtures force any construction/structure state before calling this.
  /// </summary>
  public Scene Machine(BlockPos pos, Block block, BlockEntity be) {
    World.Place(pos, block, be);
    World.Initialize(be);
    return this;
  }

  /// <summary>
  /// Fills the inclusive box between <paramref name="a"/> and <paramref name="b"/> (in any order)
  /// with a plain block - terrain, a water pond, or a solid shell around a multi-cell structure.
  /// Spans all three axes, so a 3D setup's scaffolding is one call rather than a triple loop in the test.
  /// </summary>
  public Scene Fill(BlockPos a, BlockPos b, Block block) {
    int x0 = Math.Min(a.X, b.X),
      x1 = Math.Max(a.X, b.X);
    int y0 = Math.Min(a.Y, b.Y),
      y1 = Math.Max(a.Y, b.Y);
    int z0 = Math.Min(a.Z, b.Z),
      z1 = Math.Max(a.Z, b.Z);
    for (int x = x0; x <= x1; x++)
      for (int y = y0; y <= y1; y++)
        for (int z = z0; z <= z1; z++)
          World.Place(new BlockPos(x, y, z), block);
    return this;
  }

  /// <summary>Adds every queued node to the graph (merging adjacent runs). Call once after placement.</summary>
  public Scene Build() {
    foreach (var (pos, type) in _pendingNodes)
      World.AddNode(pos, type);
    _built = true;
    return this;
  }

  /// <summary>
  /// Advances the simulation by <paramref name="seconds"/> server ticks. Each tick fires all
  /// block-entity production ticks first (machines consume/produce on their networks), then ticks all
  /// networks (flow, leak, burst, broadcast) - the same order the live server uses.
  /// </summary>
  public void Step(int seconds = 1) {
    if (!_built)
      Build();
    for (int i = 0; i < seconds; i++) {
      World.FireBlockEntityTicks();
      World.Tick(1);
    }
  }

  /// <summary>The network of type <typeparamref name="TNet"/> owning <paramref name="pos"/>, or null.</summary>
  public TNet? NetworkAt<TNet>(BlockPos pos)
    where TNet : BlockNetwork => World.NetworkAt(pos) as TNet;

  /// <summary>The block entity at <paramref name="pos"/> as <typeparamref name="TBe"/>, or null.</summary>
  public TBe? EntityAt<TBe>(BlockPos pos)
    where TBe : BlockEntity => World.GetBlockEntity(pos) as TBe;
}

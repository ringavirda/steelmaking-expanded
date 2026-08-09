using System.Runtime.CompilerServices;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.Tests;

internal static class ModuleInit {
  [ModuleInitializer]
  internal static void Init() {
    VsAssemblyResolver.Register();
    TestLang.Init();
  }
}

/// <summary>
/// Builders shared by the pipe suite: real <see cref="BlockPipe"/> instances (the burst path
/// requires the exact type), pipe runs registered with the graph, and loose <see cref="PipeNetwork"/>
/// instances for pool/merge math that does not depend on a burst ceiling.
/// </summary>
public static class PipeTestWorld {
  /// <summary>Litres a single pipe holds at 1 atm - the value the production code reads from config.</summary>
  public const float LitresPerPipe = 30f;

  /// <summary>
  /// A real <see cref="BlockPipe"/> primed with material/orientation. <c>OnLoaded</c> (which would
  /// parse these from variants) is skipped, so the protected <c>Type</c>/<c>Orientation</c> are set
  /// by reflection. One instance is shared across every cell of a straight run, as the engine does.
  /// </summary>
  public static BlockPipe MakePipe(
    string material = "iron",
    int id = 1,
    string orientation = "ns"
  ) {
    var pipe = TestBlocks.Configure(
      new BlockPipe(),
      $"ppex:pipe-straight-{material}-{orientation}",
      id,
      ("material", material),
      ("type", "straight"),
      ("orientation", orientation)
    );
    ReflectionHelpers.SetProperty(pipe, "Type", "straight");
    ReflectionHelpers.SetProperty(pipe, "Orientation", orientation);
    return pipe;
  }

  /// <summary>
  /// Builds a straight pipe run of <paramref name="length"/> cells along +Z and registers it as
  /// one network. With <paramref name="capEnds"/> the two open ends are butted against a solid
  /// (non-air) block, so the run is sealed and a gas can build pressure instead of leaking.
  /// </summary>
  public static (TestWorld world, PipeNetwork net) Run(
    int length,
    string material = "iron",
    bool capEnds = false
  ) {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    var pipe = MakePipe(material);
    for (int z = 0; z < length; z++)
      world.Place(new BlockPos(0, 0, z), pipe);

    if (capEnds) {
      var rock = TestBlocks.Configure(new Block(), "game:rock", 99);
      world.Place(new BlockPos(0, 0, -1), rock);
      world.Place(new BlockPos(0, 0, length), rock);
    }

    for (int z = 0; z < length; z++)
      world.AddNode(new BlockPos(0, 0, z), "pipe");

    var net = (PipeNetwork)world.NetworkAt(new BlockPos(0, 0, 0))!;
    return (world, net);
  }

  /// <summary>
  /// A bare <see cref="PipeNetwork"/> with <paramref name="nodeCount"/> phantom nodes (no blocks),
  /// so <c>MaxVolume = nodeCount * LitresPerPipe</c> but there is no burst ceiling. For pool and
  /// merge math that does not involve over-pressure.
  /// </summary>
  public static PipeNetwork LooseNet(
    BlockNetworkModSystem system,
    int nodeCount,
    int baseZ = 0
  ) {
    var net = new PipeNetwork(system);
    for (int i = 0; i < nodeCount; i++)
      net.Nodes.Add(new BlockPos(0, 0, baseZ + i));
    return net;
  }
}

using System.Collections.Generic;
using ExpandedLib.Blocks.Networks;

namespace ExpandedLib.Testing.Doubles;

/// <summary>
/// A minimal concrete <see cref="BlockNetworkNode"/> for graph tests: its connector set is the
/// orientation string passed in (e.g. "ns", "we", "nswe"), and its network type is configurable.
/// Bypasses the asset-load pipeline - <see cref="BlockNetworkNode.Orientation"/>/<c>Type</c> are
/// set directly rather than parsed from variants in <c>OnLoaded</c>.
/// </summary>
public sealed class TestNetworkBlock : BlockNetworkNode {
  private readonly string _networkType;

  public override string NetworkType => _networkType;

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "straight", ["ns", "we", "ud"] } };

  protected override string GetFallbackOrientation(string? type) => "ns";

  private TestNetworkBlock(string networkType, string orientation) {
    _networkType = networkType;
    Type = "straight";
    Orientation = orientation;
  }

  /// <summary>
  /// Builds and registers a node block of <paramref name="networkType"/> with the given
  /// <paramref name="orientation"/> connectors, primed with a code/id so it resolves through the
  /// store. The code defaults to a unique per-id string.
  /// </summary>
  public static TestNetworkBlock Create(
    string networkType,
    string orientation,
    int id,
    string? code = null
  ) =>
    TestBlocks.Configure(
      new TestNetworkBlock(networkType, orientation),
      code ?? $"test:{networkType}-{orientation}-{id}",
      id
    );
}

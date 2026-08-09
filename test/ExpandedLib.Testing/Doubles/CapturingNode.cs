using ExpandedLib.Blocks.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Testing.Doubles;

/// <summary>
/// A block entity that records the network broadcasts and open-connector notifications it
/// receives, so a test can assert that a producer/consumer change reached the nodes. Implements
/// <see cref="INetworkNode"/> directly (not via <c>BlockEntityNetworkNode</c>) to avoid the
/// engine-bound <c>Initialize</c> path.
/// </summary>
public sealed class CapturingNode : BlockEntity, INetworkNode {
  /// <summary>The most recent state payload delivered by <see cref="OnNetworkUpdate"/>.</summary>
  public object? LastState { get; private set; }

  /// <summary>How many broadcasts this node has received.</summary>
  public int UpdateCount { get; private set; }

  /// <summary>The most recent open-connector face set, or <c>null</c> if never notified.</summary>
  public BlockFacing[]? LastOpenFaces { get; private set; }

  public string? Orientation { get; set; }
  public string[] PossibleOrientations { get; set; } = [];
  public string NetworkType { get; set; } = "test";

  public bool HasConnectorAt(BlockFacing face) =>
    Orientation?.Contains(face.Code[0]) ?? false;

  public void OnOpenConnectorsChanged(BlockFacing[] openFaces) =>
    LastOpenFaces = openFaces;

  public void OnNetworkUpdate(object? state) {
    LastState = state;
    UpdateCount++;
  }
}

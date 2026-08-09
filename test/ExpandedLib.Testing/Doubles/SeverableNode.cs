using ExpandedLib.Blocks.Networks;

namespace ExpandedLib.Testing.Doubles;

/// <summary>
/// A <see cref="BlockEntityNetworkNode"/> whose connectivity can be toggled at runtime, for
/// testing the dynamic-sever path (a closed valve severs the graph at its own cell). Set
/// <see cref="Broken"/> and re-walk to observe the fracture. Not <c>Initialize</c>d - it is
/// attached to a block via <see cref="TestWorld.Place"/> and read directly by the graph.
/// </summary>
public sealed class SeverableNode : BlockEntityNetworkNode {
  private string _networkType = "test";

  /// <summary>When <c>true</c>, this node severs the network at its position.</summary>
  public bool Broken { get; set; }

  public override string NetworkType {
    get => _networkType;
    set => _networkType = value;
  }

  public override bool IsConnectionBroken() => Broken;
}

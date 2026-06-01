using System.Text.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace BlockNetworkLib;

/// <summary>
/// Base block entity for any block that is a node in a <see cref="BlockNetwork"/>
/// (gas pipes, molten canals, …). Handles registering/unregistering the node with
/// the <see cref="BlockNetworkModSystem"/>, persisting orientation and network
/// state, and forwarding network updates to the concrete block entity.
/// </summary>
public abstract class BlockEntityNetworkNode : BlockEntity, INetworkNode
{
  /// <summary>The network manager this node is registered with, resolved on <see cref="Initialize"/>.</summary>
  public BlockNetworkModSystem? NetworkSystem { get; protected set; }

  public override void Initialize(ICoreAPI api)
  {
    // Capture persisted state before base.Initialize() triggers AddNode.
    // AddNode may broadcast null state, which would clear _savedNetworkState
    // via OnNetworkUpdate — but we've already captured it in the local below.
    object? pendingRestore = _savedNetworkState;

    base.Initialize(api);
    NetworkSystem = api.ModLoader.GetModSystem<BlockNetworkModSystem>();

    if (api.Side == EnumAppSide.Server)
    {
      if (NetworkSystem.GetNetworkAt(Pos) == null)
        NetworkSystem.AddNode(api.World.BlockAccessor, Pos, NetworkType);

      if (
        pendingRestore != null
        && NetworkSystem.GetNetworkAt(Pos) is BlockNetwork network
      )
      {
        network.RestoreState(pendingRestore);
        network.BroadcastUpdate(api.World.BlockAccessor);
      }
    }
  }

  public override void OnBlockRemoved()
  {
    base.OnBlockRemoved();
    if (Api?.Side == EnumAppSide.Server)
      NetworkSystem?.RemoveNode(Api.World.BlockAccessor, Pos);
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetString("networkType", NetworkType);
    tree.SetString("orientation", Orientation);
    tree.SetString(
      "possibleOrientations",
      JsonSerializer.Serialize(PossibleOrientations)
    );
    SerializeNetworkState(tree, _savedNetworkState);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    NetworkType = tree.GetString("networkType", null);
    Orientation = tree.GetString("orientation");
    string? json = tree.GetString("possibleOrientations");
    PossibleOrientations =
      json != null ? JsonSerializer.Deserialize<string[]>(json) ?? [] : [];
    _savedNetworkState = DeserializeNetworkState(tree);
  }

  #region Persistence hooks — override in concrete BEs

  /// <summary>Returns <c>true</c> when <paramref name="state"/> is worth caching and restoring.
  /// Default: any non-null state.  Override to require non-empty content (e.g. amount > 0).</summary>
  protected virtual bool IsNetworkStateMeaningful(object? state) =>
    state != null;

  /// <summary>Deserializes the network state that was previously written by
  /// <see cref="SerializeNetworkState"/>.  Return <c>null</c> when no state was saved
  /// or the network should start empty.  Called from <see cref="FromTreeAttributes"/>.</summary>
  protected virtual object? DeserializeNetworkState(ITreeAttribute tree) =>
    null;

  /// <summary>Serializes <paramref name="state"/> into <paramref name="tree"/> so it
  /// survives a save/reload.  Called from <see cref="ToTreeAttributes"/>.</summary>
  protected virtual void SerializeNetworkState(
    ITreeAttribute tree,
    object? state
  ) { }

  #endregion

  #region INetworkNode
  /// <inheritdoc/>
  public string[] PossibleOrientations { get; set; } = [];

  /// <inheritdoc/>
  public string? Orientation { get; set; }

  /// <summary>Network state cached for the restore-on-load path.</summary>
  protected object? _savedNetworkState;
  protected object? _networkState;

  /// <inheritdoc/>
  public virtual bool HasConnectorAt(BlockFacing face) =>
    (Block as BlockNetworkNode)?.HasConnectorAt(face) ?? false;

  /// <inheritdoc/>
  public virtual void OnNetworkUpdate(object? state)
  {
    _networkState = state;
    if (IsNetworkStateMeaningful(state))
      _savedNetworkState = state;
    else
      _savedNetworkState = null;
  }

  /// <summary>
  /// Whether this node currently severs the network at its position (e.g. a closed
  /// valve). Default <c>false</c>; override to break connectivity dynamically.
  /// </summary>
  public virtual bool IsConnectionBroken() => false;

  /// <inheritdoc/>
  public virtual void OnOpenConnectorsChanged(BlockFacing[] openFaces) { }

  /// <inheritdoc/>
  public abstract string NetworkType { get; set; }
  #endregion
}

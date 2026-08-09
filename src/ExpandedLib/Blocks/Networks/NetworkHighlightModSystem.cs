using System.Collections.Generic;
using System.Linq;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace ExpandedLib.Blocks.Networks;

/// <summary>Toggle request sent client→server when a player runs <c>.exmod network hi|unhi</c>.</summary>
[ProtoContract]
public class NetworkHighlightRequest {
  [ProtoMember(1)]
  public bool Enable;
}

/// <summary>
/// Server-driven visualisation of every <see cref="BlockNetwork"/>: each network's blocks are
/// highlighted with transparent coloured cubes (one colour per network), so a player can see at a
/// glance which blocks share a network and where a run is broken. Toggled per-player with
/// <c>.exmod network hi</c> / <c>.exmod network unhi</c> (the command lives client-side; this system
/// carries the request to the server, which owns the graph - see <see cref="BlockEntityNetworkNode"/>,
/// where add/remove only run server-side).
/// <para>
/// The highlight tracks the live graph in near real time: a short server tick re-pushes a player's
/// highlight whenever the set of network blocks or their colours changes (block placed/broken, valve
/// opened/closed, …), but only when it actually changed, so an idle highlight sends nothing. The
/// per-network colour is derived from the network's stable <see cref="BlockNetwork.Id"/>, so a given
/// network keeps its colour across refreshes.
/// </para>
/// </summary>
public class NetworkHighlightModSystem : ModSystem {
  private const string ChannelName = "exlibNetworkHighlight";

  // Highlight group id reserved for this visualisation (kept distinct from other features' slots).
  private const int HighlightSlotId = 5318;

  // Transparent boxes so overlapping/adjacent networks stay readable.
  private const int HighlightAlpha = 0x70;

  #region Client
  private IClientNetworkChannel? _clientChannel;

  public override void StartClientSide(ICoreClientAPI api) {
    _clientChannel = api
      .Network.RegisterChannel(ChannelName)
      .RegisterMessageType<NetworkHighlightRequest>();
  }

  /// <summary>Called by the <c>.exmod network</c> command to switch this client's highlight on/off
  /// (the server does the work and pushes the highlight back).</summary>
  public void SetEnabled(bool enable) =>
    _clientChannel?.SendPacket(new NetworkHighlightRequest { Enable = enable });
  #endregion

  #region Server
  private ICoreServerAPI? _sapi;
  private BlockNetworkModSystem? _networks;

  // playerUID -> signature of the highlight data last pushed to them (for change detection).
  private readonly Dictionary<string, long> _lastPushed = [];

  public override void StartServerSide(ICoreServerAPI api) {
    _sapi = api;
    _networks = api.ModLoader.GetModSystem<BlockNetworkModSystem>();

    api.Network.RegisterChannel(ChannelName)
      .RegisterMessageType<NetworkHighlightRequest>()
      .SetMessageHandler<NetworkHighlightRequest>(OnRequest);

    api.Event.RegisterGameTickListener(OnHighlightTick, 250);
    api.Event.PlayerDisconnect += player =>
      _lastPushed.Remove(player.PlayerUID);
  }

  private void OnRequest(IServerPlayer player, NetworkHighlightRequest msg) {
    if (msg.Enable) {
      var (positions, colors, signature) = BuildHighlightData();
      _lastPushed[player.PlayerUID] = signature;
      _sapi!.World.HighlightBlocks(player, HighlightSlotId, positions, colors);
    } else {
      _lastPushed.Remove(player.PlayerUID);
      _sapi!.World.HighlightBlocks(player, HighlightSlotId, [], []);
    }
  }

  // Re-push to active players only when the graph (positions or per-network colours) changed.
  private void OnHighlightTick(float dt) {
    if (_lastPushed.Count == 0)
      return;

    var (positions, colors, signature) = BuildHighlightData();
    foreach (var uid in _lastPushed.Keys.ToList()) {
      if (_sapi!.World.PlayerByUid(uid) is not IServerPlayer player) {
        _lastPushed.Remove(uid);
        continue;
      }
      if (_lastPushed[uid] == signature)
        continue;

      _lastPushed[uid] = signature;
      player.Entity?.World.HighlightBlocks(
        player,
        HighlightSlotId,
        positions,
        colors
      );
    }
  }

  /// <summary>Builds the flat (position, colour) lists across every live network plus an
  /// order-independent signature of the result for cheap change detection.</summary>
  private (
    List<BlockPos> positions,
    List<int> colors,
    long signature
  ) BuildHighlightData() {
    var positions = new List<BlockPos>();
    var colors = new List<int>();
    long signature = positions.Count;

    foreach (var network in _networks!.AllNetworks) {
      int color = NetworkColor(network);
      foreach (var pos in network.Nodes) {
        positions.Add(pos.Copy());
        colors.Add(color);
        // XOR keeps the signature independent of enumeration order.
        signature ^= ((long)pos.GetHashCode() << 8) ^ (uint)color;
      }
    }

    signature ^= positions.Count;
    return (positions, colors, signature);
  }

  /// <summary>A stable, transparent colour for a network, derived from its
  /// <see cref="BlockNetwork.Id"/> so the same network keeps its colour between refreshes.</summary>
  private static int NetworkColor(BlockNetwork network) {
    int hue = (int)((uint)network.Id.GetHashCode() % 256);
    int rgb = ColorUtil.HsvToRgb(hue, 190, 220) & 0xFFFFFF;
    return rgb | (HighlightAlpha << 24);
  }
  #endregion
}

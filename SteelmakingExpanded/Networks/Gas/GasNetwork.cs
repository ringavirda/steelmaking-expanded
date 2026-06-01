using System;
using BlockNetworkLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Networks.Gas;

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the gas-pipe system.
/// Owns a <see cref="GasNetworkState"/> and implements all gas-specific operations:
/// production, consumption, inter-network transfer, merge/split/tick logic.
/// <para>
/// Block entities that want to inject gas call <see cref="TryProduceGas"/>;
/// consumers call <see cref="TryConsumeGas"/>.  Both methods update state and
/// broadcast automatically — no block entity needs to call
/// <c>BlockNetworkModSystem.BroadcastUpdate</c> directly.
/// </para>
/// </summary>
public class GasNetwork : BlockNetwork
{
  public override string NetworkType => "gas";

  public GasNetwork(BlockNetworkModSystem system)
    : base(system) { }

  /// <summary>
  /// Live gas state for this network, or <c>null</c> when empty. Backed by the
  /// base <see cref="BlockNetwork.State"/> so the typed accessor and any
  /// base-class code (broadcast payload, restore) always read the same object —
  /// rather than a separate shadow field the base never sees.
  /// </summary>
  public new GasNetworkState? State
  {
    get => base.State as GasNetworkState;
    private set => base.State = value;
  }

  public override void RestoreState(object? state)
  {
    State = state as GasNetworkState;
  }

  // Per-second throughput accumulators (m³). Producers/consumers add to these as
  // gas moves; OnTick folds them into State.FlowRate once a second and resets them.
  // Live on the network instance (not State) so they survive State briefly being
  // recreated, and are never serialized — they are transient counters.
  private float _producedAccum;
  private float _consumedAccum;

  #region State inheritance

  public override void InheritStateFrom(BlockNetwork source)
  {
    if (source is not GasNetwork other)
      return;
    State = other.State;
  }

  #endregion

  #region Operations

  /// <summary>
  /// Injects up to <paramref name="volume"/> m³ of gas at the given temperature
  /// and type into this network.  Returns <c>true</c> if any gas was accepted or
  /// if the gas type / temperature changed.  Broadcasts the updated state.
  /// </summary>
  public bool TryProduceGas(
    float volume,
    float temperature,
    string gasType,
    IBlockAccessor blockAccessor
  )
  {
    State ??= new GasNetworkState();
    State.MaxVolume = Nodes.Count;

    float actualVolume = Math.Min(
      volume,
      State.MaxVolume - State.CurrentVolume
    );

    if (actualVolume > 0 || State.CurrentVolume <= 0)
    {
      float totalVol = State.CurrentVolume + actualVolume;
      if (totalVol > 0)
      {
        State.SourceTemperature =
          (
            State.CurrentVolume * State.SourceTemperature
            + actualVolume * temperature
          ) / totalVol;
      }

      if (State.CurrentVolume <= 0)
        State.GasType = gasType;
      else if (actualVolume > 0)
        State.GasType = GasNetworkState.GetHigherPriorityGas(
          State.GasType,
          gasType
        );

      State.CurrentVolume += actualVolume;
      _producedAccum += actualVolume;
      BroadcastUpdate(blockAccessor);
      return true;
    }

    // Network is full — only upgrade the gas type if needed.
    if (State.GasType != gasType)
    {
      string upgraded = GasNetworkState.GetHigherPriorityGas(
        State.GasType,
        gasType
      );
      if (upgraded != State.GasType)
      {
        State.GasType = upgraded;
        BroadcastUpdate(blockAccessor);
      }
    }

    return false;
  }

  /// <summary>
  /// Withdraws up to <paramref name="requestedVolume"/> m³ from this network.
  /// Returns the actual amount consumed.  Broadcasts if volume changed.
  /// </summary>
  public float TryConsumeGas(
    float requestedVolume,
    IBlockAccessor blockAccessor
  )
  {
    if (State == null)
      return 0f;

    float available = Math.Min(requestedVolume, State.CurrentVolume);
    if (available > 0)
    {
      State.CurrentVolume -= available;
      _consumedAccum += available;
      // Note: do NOT null an emptied Air network here. Gas that is produced and
      // drained within the same second would otherwise flicker the state to null
      // (HUD reads "empty") despite flowing through. OnTick clears a truly idle
      // network instead — when both volume and throughput are zero.
      BroadcastUpdate(blockAccessor);
    }
    return available;
  }

  #endregion

  #region Merge / Split

  public override void OnMerge(BlockNetwork other, IBlockAccessor world)
  {
    if (other is not GasNetwork otherGas)
      return;

    if (otherGas.State == null)
    {
      if (State != null)
        State.MaxVolume = Nodes.Count;
      return;
    }

    if (State == null)
    {
      State = otherGas.State;
      State.MaxVolume = Nodes.Count;
      State.CurrentVolume = Math.Min(State.CurrentVolume, State.MaxVolume);
      return;
    }

    float totalVol = State.CurrentVolume + otherGas.State.CurrentVolume;
    if (totalVol > 0)
    {
      State.SourceTemperature =
        (
          State.CurrentVolume * State.SourceTemperature
          + otherGas.State.CurrentVolume * otherGas.State.SourceTemperature
        ) / totalVol;
    }

    if (State.CurrentVolume <= 0)
      State.GasType = otherGas.State.GasType;
    else if (otherGas.State.CurrentVolume > 0)
      State.GasType = GasNetworkState.GetHigherPriorityGas(
        State.GasType,
        otherGas.State.GasType
      );

    State.CurrentVolume = Math.Min(totalVol, Nodes.Count);
    State.MaxVolume = Nodes.Count;
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  )
  {
    if (original is not GasNetwork origGas || origGas.State == null)
    {
      State = null;
      return;
    }

    float volPerNode =
      origGas.State.CurrentVolume / Math.Max(1, original.Nodes.Count);
    float fragVol = Math.Min(volPerNode * Nodes.Count, Nodes.Count);

    if (fragVol <= 0f)
    {
      State = null;
      return;
    }

    State = new GasNetworkState
    {
      MaxVolume = Nodes.Count,
      CurrentVolume = fragVol,
      SourceTemperature = origGas.State.SourceTemperature,
      GasType = origGas.State.GasType,
    };
  }

  #endregion

  #region Tick

  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  )
  {
    // Throughput over the last second: the larger of what entered and what left.
    // Snapshot and reset every tick regardless of whether state exists.
    float flow = Math.Max(_producedAccum, _consumedAccum);
    _producedAccum = 0f;
    _consumedAccum = 0f;

    if (State == null)
      return;

    bool changed = false;
    int totalLeaks = 0;

    if (Math.Abs(State.FlowRate - flow) > 0.01f)
    {
      State.FlowRate = flow;
      changed = true;
    }

    // Single pass over every node: detect air-exposed open connectors (leaks),
    // notify leaking nodes, and count consumers — all in one iteration so we don't
    // walk the node set (and re-fetch each block entity) a second time below.
    int consumers = 0;
    foreach (var pos in Nodes)
    {
      var be = blockAccessor.GetBlockEntity(pos);
      if (be is IGasConsumer)
        consumers++;

      if (blockAccessor.GetBlock(pos) is not BlockNetworkNode node)
        continue;

      BlockFacing[] openFaces = manager.GetOpenConnectorFaces(
        blockAccessor,
        pos,
        node
      );
      if (openFaces.Length == 0)
        continue;

      // Compact the air-exposed faces to the front of the (freshly-allocated,
      // owned) array in place rather than allocating a LINQ Where().ToArray().
      int airOpen = 0;
      for (int i = 0; i < openFaces.Length; i++)
      {
        if (blockAccessor.GetBlock(pos.AddCopy(openFaces[i])).FirstCodePart() == "air")
          openFaces[airOpen++] = openFaces[i];
      }
      if (airOpen == 0)
        continue;

      totalLeaks += airOpen;

      if (State.CurrentVolume > 0 && be is INetworkNode nodeEntity)
        nodeEntity.OnOpenConnectorsChanged(
          airOpen == openFaces.Length ? openFaces : openFaces[..airOpen]
        );
    }

    if (State.OpeningsCount != totalLeaks)
    {
      State.OpeningsCount = totalLeaks;
      changed = true;
    }

    // Leak loss
    if (totalLeaks > 0 && State.CurrentVolume > 0)
    {
      float lost = Math.Min(State.CurrentVolume, totalLeaks * 1.0f);
      State.CurrentVolume -= lost;
      if (State.SourceTemperature > 20f)
        State.SourceTemperature = Math.Max(
          20f,
          State.SourceTemperature - totalLeaks * 5.0f
        );
      changed = true;
    }

    // Passive cooling when no consumers (count gathered in the pass above)
    if (State.CurrentVolume > 0 && State.SourceTemperature > 20f)
    {
      if (consumers == 0)
      {
        State.SourceTemperature = Math.Max(20f, State.SourceTemperature - 2.0f);
        changed = true;
      }
    }

    // Clear empty state — but only when nothing is flowing through either, so a
    // balanced push/drain network keeps its state (and reported throughput).
    if (State.CurrentVolume <= 0 && flow <= 0.01f)
    {
      State = null;
      changed = true;
    }

    if (changed)
      BroadcastUpdate(blockAccessor);
  }

  #endregion
}

using System;
using System.Collections.Generic;
using ExpandedLib.BlockNetworks;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe;

/// <summary>
/// Live state of a unified pipe run. A single connected pipe network carries two
/// independent, segregated pools:
/// <list type="bullet">
///   <item><b>Gas pool</b> — Air / Steam / Exhaust. Self-flows; pressure is the
///   volume ratio <c>CurrentVolume / MaxVolume</c> (uncapped — producers overflow
///   up to their own choke).</item>
///   <item><b>Liquid pool</b> — Water. Moves only while a pump injects; its pressure
///   is set by the pump, not the volume ratio.</item>
/// </list>
/// Temperature is tracked per-pool here; a per-node temperature field is layered on
/// top (<see cref="NodeTemperatures"/>) for spatial gradient + condensation.
/// </summary>
public class PipeNetworkState
{
  #region Gas pool

  /// <summary>Gas currently held by the network, in m³.</summary>
  public float CurrentVolume { get; set; }

  /// <summary>Maximum gas the network can hold at 1 atm (1 m³ per pipe node).</summary>
  public float MaxVolume { get; set; }

  /// <summary>Temperature (°C) of the gas as injected by the producing source.</summary>
  public float SourceTemperature { get; set; }

  /// <summary>Current gas kind: "Air", "Steam", or "Exhaust".</summary>
  public string GasType { get; set; } = "Air";

  /// <summary>Gas pressure in atm — <c>CurrentVolume / MaxVolume</c>, uncapped.</summary>
  public float Pressure { get; set; }

  #endregion

  #region Liquid pool

  /// <summary>Water currently held by the network, in m³.</summary>
  public float WaterVolume { get; set; }

  /// <summary>Temperature (°C) of the water in the liquid pool.</summary>
  public float WaterTemperature { get; set; } = 20f;

  /// <summary>Liquid pressure in atm — set by the pump, not the volume ratio.</summary>
  public float LiquidPressure { get; set; }

  #endregion

  #region Shared

  /// <summary>Number of open-ended connectors (leaks) on the network.</summary>
  public int OpeningsCount { get; set; } = 0;

  /// <summary>
  /// Gas throughput in m³/s — the volume that moved through the network over the
  /// last second (max of produced and consumed). Computed once per second by
  /// <see cref="PipeNetwork.OnTick"/>.
  /// </summary>
  public float FlowRate { get; set; } = 0f;

  /// <summary>
  /// Per-node temperature field (°C), propagated from producer nodes outward with
  /// per-block heat loss. <c>null</c> until the first tick computes it. Each pipe
  /// block entity reads its own <see cref="BlockPos"/> entry to drive its local
  /// display, glow, and condensation.
  /// </summary>
  public Dictionary<BlockPos, float>? NodeTemperatures { get; set; }

  /// <summary>Whether the network has any open-ended connectors.</summary>
  public bool IsLeaking => OpeningsCount > 0;

  /// <summary>
  /// Returns the higher-priority gas of two types when networks mix
  /// (Exhaust &gt; Air). Steam ranks with Air (it is just hot Air-pool content).
  /// </summary>
  public static string GetHigherPriorityGas(string type1, string type2)
  {
    if (type1 == "Exhaust" || type2 == "Exhaust")
      return "Exhaust";
    if (type1 == "Steam" || type2 == "Steam")
      return "Steam";
    return "Air";
  }

  /// <summary>Gas pressure (atm) for a given pool state.</summary>
  public static float ComputeGasPressure(
    float currentVolume,
    float maxVolume
  ) => maxVolume > 0f ? currentVolume / maxVolume : 0f;

  #endregion
}

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the unified pipe system. Owns a
/// <see cref="PipeNetworkState"/> with segregated gas and liquid pools and
/// implements production, consumption, pressure, merge/split and tick logic.
/// <para>
/// Gas producers call <see cref="TryProduceGas"/> (with an optional
/// <c>maxOutputPressure</c> choke); gas consumers call <see cref="TryConsumeGas"/>.
/// Water producers (the pump) call <see cref="TryProduceLiquid"/> and set the
/// liquid pressure; water consumers (the boiler) call <see cref="TryConsumeLiquid"/>.
/// </para>
/// </summary>
public class PipeNetwork : BlockNetwork
{
  public override string NetworkType => "pipe";

  public PipeNetwork(BlockNetworkModSystem system)
    : base(system) { }

  /// <summary>
  /// Live pipe state for this network, or <c>null</c> when empty. Backed by the
  /// base <see cref="BlockNetwork.State"/> so the typed accessor and base-class
  /// code always read the same object.
  /// </summary>
  public new PipeNetworkState? State
  {
    get => base.State as PipeNetworkState;
    private set => base.State = value;
  }

  public override void RestoreState(object? state)
  {
    State = state as PipeNetworkState;
  }

  // Per-second throughput accumulators (m³). Producers/consumers add to these;
  // OnTick folds them into State.FlowRate once a second and resets them.
  private float _producedAccum;
  private float _consumedAccum;

  // Active gas-injection points (node position → injected temperature). Producers
  // register themselves here when they actually inject gas; entries age out a few
  // ticks after production stops so the temperature gradient fades smoothly rather
  // than snapping to ambient. The per-cell temperature field is a BFS from these.
  private readonly Dictionary<BlockPos, GasSource> _gasSources = [];

  private struct GasSource
  {
    public float Temperature;
    public int Age;
  }

  #region State inheritance

  public override void InheritStateFrom(BlockNetwork source)
  {
    if (source is not PipeNetwork other)
      return;
    State = other.State;
  }

  #endregion

  #region Gas pool

  /// <summary>
  /// Injects up to <paramref name="volume"/> m³ of gas into the network. Gas may
  /// overflow above 1 atm up to <paramref name="maxOutputPressure"/> · MaxVolume —
  /// each producer enforces its own choke (intake 1.0, boiler 15, blower f(power)).
  /// Returns <c>true</c> if any gas was accepted or the type/temperature changed.
  /// </summary>
  public bool TryProduceGas(
    float volume,
    float temperature,
    string gasType,
    IBlockAccessor blockAccessor,
    float maxOutputPressure = 1f,
    BlockPos? sourcePos = null
  )
  {
    State ??= new PipeNetworkState();
    State.MaxVolume = Nodes.Count;

    float ceiling = maxOutputPressure * State.MaxVolume;
    float actualVolume = Math.Min(volume, ceiling - State.CurrentVolume);

    // Register this node as a hot-gas injection point for the per-cell temperature
    // field (BFS source in OnTick), even if the run is momentarily choked.
    if (sourcePos != null && Nodes.Contains(sourcePos))
      _gasSources[sourcePos] = new GasSource
      {
        Temperature = temperature,
        Age = 0,
      };

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
        State.GasType = PipeNetworkState.GetHigherPriorityGas(
          State.GasType,
          gasType
        );

      if (actualVolume > 0)
      {
        State.CurrentVolume += actualVolume;
        _producedAccum += actualVolume;
      }
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.CurrentVolume,
        State.MaxVolume
      );
      BroadcastUpdate(blockAccessor);
      return true;
    }

    // Network is at its choke — only upgrade the gas type if needed.
    if (State.GasType != gasType)
    {
      string upgraded = PipeNetworkState.GetHigherPriorityGas(
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
  /// Withdraws up to <paramref name="requestedVolume"/> m³ of gas from this network.
  /// Returns the actual amount consumed. Broadcasts if volume changed.
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
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.CurrentVolume,
        State.MaxVolume
      );
      BroadcastUpdate(blockAccessor);
    }
    return available;
  }

  #endregion

  #region Liquid pool

  /// <summary>
  /// Injects up to <paramref name="volume"/> m³ of water into the liquid pool and
  /// sets the liquid pressure (the pump drives both). Water temperature blends
  /// volume-weighted. Returns <c>true</c> if any water was accepted.
  /// </summary>
  /// <summary>Water (m³) a single pipe can hold — the liquid pool is metered in m³, like the gas pool.</summary>
  public const float WaterPerNode = 0.1f;

  public bool TryProduceLiquid(
    float volume,
    float temperature,
    float setPressure,
    IBlockAccessor blockAccessor
  )
  {
    State ??= new PipeNetworkState();
    State.MaxVolume = Nodes.Count;

    float actual = Math.Min(
      volume,
      Nodes.Count * WaterPerNode - State.WaterVolume
    );
    if (actual <= 0f)
      return false;

    float total = State.WaterVolume + actual;
    if (total > 0)
      State.WaterTemperature =
        (State.WaterVolume * State.WaterTemperature + actual * temperature)
        / total;

    State.WaterVolume = total;
    State.LiquidPressure = setPressure;
    _producedAccum += actual;
    BroadcastUpdate(blockAccessor);
    return true;
  }

  /// <summary>
  /// Withdraws up to <paramref name="requestedVolume"/> m³ of water from the liquid
  /// pool. Returns the actual amount consumed (carrying <see cref="PipeNetworkState.WaterTemperature"/>).
  /// </summary>
  public float TryConsumeLiquid(
    float requestedVolume,
    IBlockAccessor blockAccessor
  )
  {
    if (State == null)
      return 0f;

    float available = Math.Min(requestedVolume, State.WaterVolume);
    if (available > 0)
    {
      State.WaterVolume -= available;
      _consumedAccum += available;
      if (State.WaterVolume <= 0f)
        State.LiquidPressure = 0f;
      BroadcastUpdate(blockAccessor);
    }
    return available;
  }

  #endregion

  #region Merge / Split

  public override void OnMerge(BlockNetwork other, IBlockAccessor world)
  {
    if (other is not PipeNetwork otherPipe)
      return;

    if (otherPipe.State == null)
    {
      if (State != null)
        State.MaxVolume = Nodes.Count;
      return;
    }

    if (State == null)
    {
      State = otherPipe.State;
      State.MaxVolume = Nodes.Count;
      State.CurrentVolume = Math.Min(State.CurrentVolume, State.MaxVolume);
      State.WaterVolume = Math.Min(
        State.WaterVolume,
        Nodes.Count * WaterPerNode
      );
      return;
    }

    // Gas pool merge (volume-weighted temperature, priority-resolved type).
    float totalGas = State.CurrentVolume + otherPipe.State.CurrentVolume;
    if (totalGas > 0)
    {
      State.SourceTemperature =
        (
          State.CurrentVolume * State.SourceTemperature
          + otherPipe.State.CurrentVolume * otherPipe.State.SourceTemperature
        ) / totalGas;
    }
    if (State.CurrentVolume <= 0)
      State.GasType = otherPipe.State.GasType;
    else if (otherPipe.State.CurrentVolume > 0)
      State.GasType = PipeNetworkState.GetHigherPriorityGas(
        State.GasType,
        otherPipe.State.GasType
      );
    State.CurrentVolume = Math.Min(totalGas, Nodes.Count);

    // Liquid pool merge (volume-weighted temperature, max of the two pressures).
    float totalWater = State.WaterVolume + otherPipe.State.WaterVolume;
    if (totalWater > 0)
    {
      State.WaterTemperature =
        (
          State.WaterVolume * State.WaterTemperature
          + otherPipe.State.WaterVolume * otherPipe.State.WaterTemperature
        ) / totalWater;
    }
    State.WaterVolume = Math.Min(totalWater, Nodes.Count * WaterPerNode);
    State.LiquidPressure = Math.Max(
      State.LiquidPressure,
      otherPipe.State.LiquidPressure
    );

    State.MaxVolume = Nodes.Count;
    State.Pressure = PipeNetworkState.ComputeGasPressure(
      State.CurrentVolume,
      State.MaxVolume
    );
  }

  public override void OnSplitFragment(
    BlockNetwork original,
    IBlockAccessor world
  )
  {
    if (original is not PipeNetwork origPipe || origPipe.State == null)
    {
      State = null;
      return;
    }

    int origCount = Math.Max(1, original.Nodes.Count);
    float gasPerNode = origPipe.State.CurrentVolume / origCount;
    float waterShare = origPipe.State.WaterVolume / origCount;
    float fragGas = Math.Min(gasPerNode * Nodes.Count, Nodes.Count);
    float fragWater = Math.Min(
      waterShare * Nodes.Count,
      Nodes.Count * WaterPerNode
    );

    if (fragGas <= 0f && fragWater <= 0f)
    {
      State = null;
      return;
    }

    State = new PipeNetworkState
    {
      MaxVolume = Nodes.Count,
      CurrentVolume = fragGas,
      SourceTemperature = origPipe.State.SourceTemperature,
      GasType = origPipe.State.GasType,
      WaterVolume = fragWater,
      WaterTemperature = origPipe.State.WaterTemperature,
      LiquidPressure = fragWater > 0f ? origPipe.State.LiquidPressure : 0f,
    };
    State.Pressure = PipeNetworkState.ComputeGasPressure(
      State.CurrentVolume,
      State.MaxVolume
    );
  }

  #endregion

  #region Tick

  public override void OnTick(
    IBlockAccessor blockAccessor,
    float dt,
    BlockNetworkModSystem manager
  )
  {
    float flow = Math.Max(_producedAccum, _consumedAccum);
    _producedAccum = 0f;
    _consumedAccum = 0f;

    if (State == null)
      return;

    bool changed = false;

    State.MaxVolume = Nodes.Count;
    float newPressure = PipeNetworkState.ComputeGasPressure(
      State.CurrentVolume,
      State.MaxVolume
    );
    if (Math.Abs(State.Pressure - newPressure) > 0.02f)
    {
      State.Pressure = newPressure;
      changed = true;
    }

    if (Math.Abs(State.FlowRate - flow) > 0.01f)
    {
      State.FlowRate = flow;
      changed = true;
    }

    // Per-cell temperature field (BFS from active gas-injection points) and the
    // steam→water condensation it drives. Both mutate state, so flag a broadcast.
    UpdateNodeTemperatures(blockAccessor, manager);
    if (ApplyCondensation())
      changed = true;

    // Single pass: detect open connectors and classify each — a vanilla chimney atop
    // the connector is a gas vent (not a leak); an air-exposed end is a leak (gas
    // bleeds, and liquid sprays out as a splash); count gas consumers too.
    int consumers = 0;
    int totalLeaks = 0;
    int chimneyVents = 0;
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

      int airOpen = 0;
      for (int i = 0; i < openFaces.Length; i++)
      {
        Block neighbour = blockAccessor.GetBlock(pos.AddCopy(openFaces[i]));
        // A chimney capping the pipe vents gas to the sky (no leak). Matched by code so
        // it works for vanilla chimneys and any other mod's chimney variants.
        if (neighbour.Code?.Path.Contains("chimney") == true)
        {
          chimneyVents++;
          continue;
        }
        if (neighbour.FirstCodePart() == "air")
          openFaces[airOpen++] = openFaces[i];
      }
      if (airOpen == 0)
        continue;

      totalLeaks += airOpen;

      if (be is INetworkNode nodeEntity)
      {
        BlockFacing[] leakFaces =
          airOpen == openFaces.Length ? openFaces : openFaces[..airOpen];
        if (State.CurrentVolume > 0)
          nodeEntity.OnOpenConnectorsChanged(leakFaces);
        // Water sprays out of the open end like a poured bucket.
        if (State.WaterVolume > 0 && be is BlockEntityPipe pipeBe)
          pipeBe.SpawnLiquidLeak(leakFaces);
      }
    }

    if (State.OpeningsCount != totalLeaks)
    {
      State.OpeningsCount = totalLeaks;
      changed = true;
    }

    // Chimney venting (gas pool only) — 2 m³/s per chimney-capped opening.
    if (chimneyVents > 0 && State.CurrentVolume > 0)
    {
      float vented = Math.Min(
        State.CurrentVolume,
        chimneyVents * PpexValues.ChimneyVentRate
      );
      State.CurrentVolume -= vented;
      _consumedAccum += vented;
      changed = true;
    }

    // Leak loss (gas pool)
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

    // Leak loss (liquid pool) — open ends bleed water at LiquidLeakRatePerOpening each.
    if (totalLeaks > 0 && State.WaterVolume > 0)
    {
      float lostWater = Math.Min(
        State.WaterVolume,
        totalLeaks * PpexValues.LiquidLeakRatePerOpening
      );
      State.WaterVolume -= lostWater;
      if (State.WaterVolume <= 0f)
        State.LiquidPressure = 0f;
      changed = true;
    }

    // Keep the broadcast gas pressure in step after venting / leaking.
    if (chimneyVents > 0 || totalLeaks > 0)
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.CurrentVolume,
        State.MaxVolume
      );

    // Passive cooling when no consumers
    if (
      State.CurrentVolume > 0
      && State.SourceTemperature > 20f
      && consumers == 0
    )
    {
      State.SourceTemperature = Math.Max(20f, State.SourceTemperature - 2.0f);
      changed = true;
    }

    // Clear empty state — only when nothing is flowing through either pool.
    if (State.CurrentVolume <= 0 && State.WaterVolume <= 0 && flow <= 0.01f)
    {
      State = null;
      changed = true;
    }

    if (changed)
      BroadcastUpdate(blockAccessor);

    // Over-pressure / over-temperature failures — collected above, executed last so
    // we never mutate the node set while reading it. Each burst removes a node
    // (fracturing the run) and drops the pipe's materials (hot, for a melt).
    if (State != null)
    {
      var bursts = CollectBursts(blockAccessor);
      foreach (var (pos, temp) in bursts)
        ExecuteBurst(pos, temp, blockAccessor, manager);
    }
  }

  private static readonly Random _rand = new();

  /// <summary>
  /// Finds pipes that should fail this tick: any non-ferric pipe whose local
  /// temperature exceeds its melting point (all of them melt), plus — when the gas
  /// pressure exceeds the weakest pipe's burst rating — one random over-rated pipe.
  /// </summary>
  private List<(BlockPos pos, float temp)> CollectBursts(IBlockAccessor world)
  {
    var result = new List<(BlockPos, float)>();
    if (State == null)
      return result;

    float minBurst = float.MaxValue;
    foreach (var pos in Nodes)
      if (world.GetBlock(pos) is BlockPipe p)
        minBurst = Math.Min(minBurst, p.BurstPressure);

    bool overPressure =
      State.CurrentVolume > 0f
      && minBurst < float.MaxValue
      && State.Pressure > minBurst;

    var temps = State.NodeTemperatures;
    var pressureCandidates = new List<(BlockPos, float)>();

    foreach (var pos in Nodes)
    {
      if (world.GetBlock(pos) is not BlockPipe pipe)
        continue;
      float t =
        temps != null && temps.TryGetValue(pos, out float v)
          ? v
          : State.SourceTemperature;

      if (t > pipe.MeltingPoint)
        result.Add((pos, t)); // thermal melt — drops hot
      else if (overPressure && State.Pressure > pipe.BurstPressure)
        pressureCandidates.Add((pos, t));
    }

    if (overPressure && pressureCandidates.Count > 0)
      result.Add(pressureCandidates[_rand.Next(pressureCandidates.Count)]);

    return result;
  }

  /// <summary>
  /// Breaks a failed pipe: drops its materials (with <paramref name="dropTemp"/> as
  /// the item temperature, so a melt drops hot metal), removes it from the graph,
  /// and sets the cell to air. The graph removal handles the network fracture.
  /// </summary>
  private static void ExecuteBurst(
    BlockPos pos,
    float dropTemp,
    IBlockAccessor world,
    BlockNetworkModSystem manager
  )
  {
    Block block = world.GetBlock(pos);
    if (block.BlockId == 0)
      return;

    var sworld = manager.ServerWorld;
    if (sworld != null)
    {
      ItemStack[]? drops = block.GetDrops(sworld, pos, null);
      if (drops != null)
      {
        foreach (var ds in drops)
        {
          if (dropTemp > 20f)
            ds.Collectible.SetTemperature(
              sworld,
              ds,
              dropTemp,
              delayCooldown: false
            );
          sworld.SpawnItemEntity(ds, pos.ToVec3d().Add(0.5, 0.5, 0.5));
        }
      }
    }

    manager.RemoveNode(world, pos);
    world.SetBlock(0, pos);
  }

  /// <summary>
  /// Rebuilds <see cref="PipeNetworkState.NodeTemperatures"/> as a multi-source BFS
  /// from the active gas-injection points: each node takes the hottest reachable
  /// source minus the per-pipe heat loss of the pipe being entered (ferric pipes lose
  /// little, copper/brass lose a lot), floored at ambient. Nodes beyond the heat's
  /// reach are absent (treated as ambient).
  /// </summary>
  private void UpdateNodeTemperatures(
    IBlockAccessor world,
    BlockNetworkModSystem manager
  )
  {
    const float ambient = 20f;

    // Age out stale sources so the gradient fades a few ticks after production stops.
    if (_gasSources.Count > 0)
    {
      var stale = new List<BlockPos>();
      foreach (var kv in _gasSources)
      {
        var s = kv.Value;
        s.Age++;
        if (s.Age > 3 || !Nodes.Contains(kv.Key))
          stale.Add(kv.Key);
        else
          _gasSources[kv.Key] = s;
      }
      foreach (var p in stale)
        _gasSources.Remove(p);
    }

    var temps = State!.NodeTemperatures ??= [];
    temps.Clear();

    if (_gasSources.Count == 0)
      return;

    var queue = new Queue<BlockPos>();
    foreach (var kv in _gasSources)
    {
      float t = Math.Max(ambient, kv.Value.Temperature);
      if (!temps.TryGetValue(kv.Key, out float cur) || t > cur)
      {
        temps[kv.Key] = t;
        queue.Enqueue(kv.Key);
      }
    }

    while (queue.Count > 0)
    {
      var pos = queue.Dequeue();
      float here = temps[pos];
      foreach (
        var adj in manager.GetConnectedNeighbors(world, pos, NetworkType)
      )
      {
        if (!Nodes.Contains(adj))
          continue;
        // Heat is lost crossing into the next pipe, scaled by that pipe's material.
        float next = here - HeatLossAt(world, adj);
        if (next <= ambient)
          continue;
        if (!temps.TryGetValue(adj, out float cur) || next > cur)
        {
          temps[adj] = next;
          queue.Enqueue(adj);
        }
      }
    }
  }

  /// <summary>Per-pipe heat loss (°C) at <paramref name="pos"/> — low for ferric pipes,
  /// high for copper/brass. Non-pipe nodes (brick passthrough/outlet) count as ferric.</summary>
  private static float HeatLossAt(IBlockAccessor world, BlockPos pos) =>
    world.GetBlock(pos) is BlockPipe { IsFerric: false }
      ? PpexValues.NonFerricPipeHeatLoss
      : PpexValues.FerricPipeHeatLoss;

  /// <summary>
  /// Condenses steam whose local temperature has fallen below the boiling point:
  /// the cold fraction of the gas (steam) pool is transferred into the liquid
  /// (water) pool at the local temperature. Returns <c>true</c> if anything moved.
  /// </summary>
  private bool ApplyCondensation()
  {
    if (State == null || State.GasType != "Steam" || State.CurrentVolume <= 0f)
      return false;

    const float ambient = 20f;
    var temps = State.NodeTemperatures;
    int total = Math.Max(1, Nodes.Count);
    int below = 0;
    float coldSum = 0f;

    foreach (var pos in Nodes)
    {
      float t =
        temps != null && temps.TryGetValue(pos, out float v) ? v : ambient;
      if (t < PpexValues.BoilingPoint)
      {
        below++;
        coldSum += t;
      }
    }

    if (below == 0)
      return false;

    float frac = (float)below / total;
    float condensed = State.CurrentVolume * frac;
    if (condensed <= 0f)
      return false;

    float condTemp = coldSum / below;
    State.CurrentVolume -= condensed;

    // Steam collapses back to its (much smaller) water volume — the same ratio the
    // boiler used to make it — so a condense/boil loop conserves water.
    float water = condensed * PpexValues.BoilerWaterPerSteam;
    float totalWater = State.WaterVolume + water;
    if (totalWater > 0f)
      State.WaterTemperature =
        (State.WaterVolume * State.WaterTemperature + water * condTemp)
        / totalWater;
    State.WaterVolume = Math.Min(totalWater, Nodes.Count * WaterPerNode);

    State.Pressure = PipeNetworkState.ComputeGasPressure(
      State.CurrentVolume,
      State.MaxVolume
    );
    return true;
  }

  #endregion
}

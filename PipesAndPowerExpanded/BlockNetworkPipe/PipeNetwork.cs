using System;
using System.Collections.Generic;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe;

/// <summary>
/// Live state of a pipe run. A connected pipe network carries exactly ONE medium at a
/// time — a gas (Air / Steam / Exhaust) or a liquid (Water) — never both. The medium is
/// claimed by the first producer and held until the run empties completely. Gas and
/// liquid keep their own physics: a gas's <see cref="Pressure"/> is the volume ratio
/// <c>Volume / MaxVolume</c> (uncapped — producers overflow up to their own choke),
/// while a liquid's pressure is set by the pump that drives it. Temperature is a single
/// network-wide value (no spatial gradient — every pipe reports the same).
/// </summary>
public class PipeNetworkState
{
  /// <summary>Content currently held by the network, in litres (gas or water).</summary>
  public float Volume { get; set; }

  /// <summary>Maximum the network can hold at 1 atm (<see cref="PpexValues.LitresPerPipe"/> per pipe node).</summary>
  public float MaxVolume { get; set; }

  /// <summary>Temperature (°C) of the content, injected by the producing source.</summary>
  public float Temperature { get; set; } = 20f;

  /// <summary>Current medium: "Air", "Steam", "Exhaust", "Water", or "" when empty.</summary>
  public string MediumType { get; set; } = "";

  /// <summary>Pressure in atm — for a gas, <c>Volume / MaxVolume</c> (uncapped); for a
  /// liquid, the value set by the pump.</summary>
  public float Pressure { get; set; }

  /// <summary>Number of open-ended connectors (leaks) on the network.</summary>
  public int OpeningsCount { get; set; } = 0;

  /// <summary>
  /// Throughput in L/s — the volume that moved through the network over the last second
  /// (max of produced and consumed). Computed once per second by <see cref="PipeNetwork.OnTick"/>.
  /// Marks a live line even when the run sits near 0 L because a producer feeds it and a
  /// consumer drains it at the same rate each tick.
  /// </summary>
  public float FlowRate { get; set; } = 0f;

  /// <summary>Whether the network currently carries a liquid (water) rather than a gas.</summary>
  public bool IsLiquid => MediumType == "Water";

  /// <summary>Whether the network has any open-ended connectors.</summary>
  public bool IsLeaking => OpeningsCount > 0;

  /// <summary>Whether <paramref name="medium"/> can be produced into a run currently
  /// carrying <paramref name="current"/> — same family only (gases mix; water is its own
  /// medium), or an empty run that hasn't claimed a medium yet.</summary>
  public static bool MediaCompatible(string current, string medium) =>
    current.Length == 0 || (current == "Water") == (medium == "Water");

  /// <summary>
  /// Returns the higher-priority gas of two types when gas runs merge
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
}

/// <summary>
/// Concrete <see cref="BlockNetwork"/> for the pipe system. Owns a single-medium
/// <see cref="PipeNetworkState"/> and implements production, consumption, pressure,
/// merge/split and tick logic.
/// <para>
/// Gas producers call <see cref="TryProduceGas"/> (with an optional
/// <c>maxOutputPressure</c> choke); gas consumers call <see cref="TryConsumeGas"/>.
/// Water producers (the pump) call <see cref="TryProduceLiquid"/> and set the
/// liquid pressure; water consumers (the boiler) call <see cref="TryConsumeLiquid"/>.
/// Each pair refuses to act on a run already carrying the other medium, so a run only
/// ever holds gas or liquid.
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

  // Per-second throughput accumulators (litres). Producers/consumers add to these;
  // OnTick folds them into State.FlowRate once a second and resets them.
  private float _producedAccum;
  private float _consumedAccum;

  // The raw per-tick throughput is bursty: a boiler tops up its whole intake buffer
  // (hundreds of litres) in a single draw, then sits idle for many ticks, so the readout
  // would otherwise swing between "empty" and 100+ L/s second to second. We display an
  // exponential moving average instead, and only let a drained run clear back to "empty"
  // after a few seconds with no real flow — so the medium label survives brief drains.
  private float _smoothedFlow;
  private float _secondsSinceFlow;
  private const float FlowSmoothingAlpha = 0.3f;
  private const float EmptyClearDelaySeconds = 3f;

  // Last fire-loop start time (world ms) per drawing chimney, so the continuous draught
  // sound restarts seamlessly (just under the 9.26 s clip) instead of stacking every tick.
  private const long ChimneyFireLoopMs = 9000;
  private readonly Dictionary<BlockPos, long> _chimneyFireMs = new();

  // In-game day stamp for natural water evaporation (see OnTick). -1 until the first
  // tick stamps it, so no evaporation is charged for time the network was unloaded.
  private double _lastEvapDays = -1;

  // Seconds the run has sat at (or above) its weakest pipe's burst pressure with no
  // relief. Counts up in OnTick; at PipeOverpressureSeconds a pipe bursts. Transient —
  // a reload resets the grace, which is fine since an unloaded run isn't pressurising.
  private float _overpressureSeconds;

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
  /// Injects up to <paramref name="volume"/> litres of gas into the network. Gas may
  /// overflow above 1 atm up to <paramref name="maxOutputPressure"/> · MaxVolume —
  /// each producer enforces its own choke (intake 1.0, boiler/blower from steam pressure).
  /// Returns <c>true</c> if any gas was accepted or the type/temperature changed.
  /// </summary>
  public bool TryProduceGas(
    float volume,
    float temperature,
    string gasType,
    IBlockAccessor blockAccessor,
    float maxOutputPressure = 1f,
    bool bypassLeakCap = false
  )
  {
    State ??= new PipeNetworkState();
    // A run already carrying water rejects gas — one medium per network.
    if (!PipeNetworkState.MediaCompatible(State.MediumType, gasType))
      return false;
    State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;

    // The run physically cannot be charged past the weakest pipe's burst rating, and a
    // leaking run vents anything over 1 atm — so a producer's own choke is clamped by
    // both before it ever decides how much gas the network can swallow this tick.
    // <paramref name="bypassLeakCap"/> lifts the 1-atm clamp so a caller that hand-limits its
    // <paramref name="volume"/> to the run's leak rate can push that trickle straight through
    // a leaking run (it flows in and leaks back out) without it backing up.
    float ceilingPressure = Math.Min(
      maxOutputPressure,
      MinBurstPressure(blockAccessor)
    );
    if (State.IsLeaking && !bypassLeakCap)
      ceilingPressure = Math.Min(ceilingPressure, 1f);

    float ceiling = ceilingPressure * State.MaxVolume;
    float actualVolume = Math.Min(volume, ceiling - State.Volume);

    if (actualVolume > 0 || State.Volume <= 0)
    {
      float totalVol = State.Volume + actualVolume;
      if (totalVol > 0)
      {
        State.Temperature =
          (State.Volume * State.Temperature + actualVolume * temperature)
          / totalVol;
      }

      if (State.Volume <= 0)
        State.MediumType = gasType;
      else if (actualVolume > 0)
        State.MediumType = PipeNetworkState.GetHigherPriorityGas(
          State.MediumType,
          gasType
        );

      if (actualVolume > 0)
      {
        State.Volume += actualVolume;
        _producedAccum += actualVolume;
      }
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );
      BroadcastUpdate(blockAccessor);
      return true;
    }

    // Network is at its choke — only upgrade the gas type if needed.
    if (State.MediumType != gasType)
    {
      string upgraded = PipeNetworkState.GetHigherPriorityGas(
        State.MediumType,
        gasType
      );
      if (upgraded != State.MediumType)
      {
        State.MediumType = upgraded;
        BroadcastUpdate(blockAccessor);
      }
    }

    return false;
  }

  /// <summary>
  /// Same as <see cref="TryProduceGas"/> but returns the litres actually accepted
  /// (0 when nothing fit or the medium was rejected). The canonical call for producers
  /// that must know the accepted volume — boiler steam push, pressure-valve overflow —
  /// instead of each measuring a before/after <c>State.Volume</c> delta themselves.
  /// </summary>
  public float ProduceGasMeasured(
    float volume,
    float temperature,
    string gasType,
    IBlockAccessor blockAccessor,
    float maxOutputPressure = 1f,
    bool bypassLeakCap = false
  )
  {
    float before = State?.Volume ?? 0f;
    TryProduceGas(
      volume,
      temperature,
      gasType,
      blockAccessor,
      maxOutputPressure,
      bypassLeakCap
    );
    return Math.Max(0f, (State?.Volume ?? 0f) - before);
  }

  /// <summary>
  /// Withdraws up to <paramref name="requestedVolume"/> litres of gas from this network.
  /// Returns the actual amount consumed (0 on a water run). Broadcasts if volume changed.
  /// </summary>
  public float TryConsumeGas(
    float requestedVolume,
    IBlockAccessor blockAccessor
  )
  {
    if (State == null || State.IsLiquid)
      return 0f;

    float available = Math.Min(requestedVolume, State.Volume);
    if (available > 0)
    {
      State.Volume -= available;
      _consumedAccum += available;
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );
      BroadcastUpdate(blockAccessor);
    }
    return available;
  }

  #endregion

  #region Liquid pool

  /// <summary>
  /// Injects up to <paramref name="volume"/> litres of water into the network and sets
  /// the liquid pressure (the pump drives both). Water temperature blends volume-weighted.
  /// Returns <c>true</c> if any water was accepted. Refuses a run already carrying gas.
  /// </summary>
  public bool TryProduceLiquid(
    float volume,
    float temperature,
    float setPressure,
    IBlockAccessor blockAccessor
  )
  {
    State ??= new PipeNetworkState();
    // A run already carrying gas rejects water — one medium per network.
    if (!PipeNetworkState.MediaCompatible(State.MediumType, "Water"))
      return false;
    State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;

    float actual = Math.Min(volume, State.MaxVolume - State.Volume);
    if (actual <= 0f)
      return false;

    float total = State.Volume + actual;
    if (total > 0)
      State.Temperature =
        (State.Volume * State.Temperature + actual * temperature) / total;

    State.MediumType = "Water";
    State.Volume = total;
    State.Pressure = setPressure;
    _producedAccum += actual;
    BroadcastUpdate(blockAccessor);
    return true;
  }

  /// <summary>
  /// Same as <see cref="TryProduceLiquid"/> but returns the litres actually accepted.
  /// The canonical call for producers that must know the accepted volume (fluid
  /// intake), instead of measuring a before/after <c>State.Volume</c> delta.
  /// </summary>
  public float ProduceLiquidMeasured(
    float volume,
    float temperature,
    float setPressure,
    IBlockAccessor blockAccessor
  )
  {
    float before = State?.Volume ?? 0f;
    TryProduceLiquid(volume, temperature, setPressure, blockAccessor);
    return Math.Max(0f, (State?.Volume ?? 0f) - before);
  }

  /// <summary>
  /// Withdraws up to <paramref name="requestedVolume"/> litres of water from the network.
  /// Returns the actual amount consumed (0 on a gas run), carrying <see cref="PipeNetworkState.Temperature"/>.
  /// </summary>
  public float TryConsumeLiquid(
    float requestedVolume,
    IBlockAccessor blockAccessor
  )
  {
    if (State == null || !State.IsLiquid)
      return 0f;

    float available = Math.Min(requestedVolume, State.Volume);
    if (available > 0)
    {
      State.Volume -= available;
      _consumedAccum += available;
      if (State.Volume <= 0f)
        State.Pressure = 0f;
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
        State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;
      return;
    }

    if (State == null)
    {
      State = otherPipe.State;
      State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;
      State.Volume = Math.Min(State.Volume, State.MaxVolume);
      return;
    }

    State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;

    // Two runs of incompatible media (gas joined to water) can't blend — the larger one
    // wins the merged run and the smaller's content is discarded (it can't coexist).
    if (
      !PipeNetworkState.MediaCompatible(
        State.MediumType,
        otherPipe.State.MediumType
      )
    )
    {
      if (otherPipe.State.Volume > State.Volume)
        State = otherPipe.State;
      State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;
      State.Volume = Math.Min(State.Volume, State.MaxVolume);
      if (!State.IsLiquid)
        State.Pressure = PipeNetworkState.ComputeGasPressure(
          State.Volume,
          State.MaxVolume
        );
      return;
    }

    // Same medium — blend volume-weighted temperature and combine volume.
    float total = State.Volume + otherPipe.State.Volume;
    if (total > 0)
    {
      State.Temperature =
        (
          State.Volume * State.Temperature
          + otherPipe.State.Volume * otherPipe.State.Temperature
        ) / total;
    }

    // Gas runs resolve the dominant type by priority; water keeps its medium.
    if (!State.IsLiquid)
    {
      if (State.Volume <= 0)
        State.MediumType = otherPipe.State.MediumType;
      else if (otherPipe.State.Volume > 0)
        State.MediumType = PipeNetworkState.GetHigherPriorityGas(
          State.MediumType,
          otherPipe.State.MediumType
        );
    }

    State.Volume = Math.Min(total, State.MaxVolume);
    State.Pressure = State.IsLiquid
      ? Math.Max(State.Pressure, otherPipe.State.Pressure)
      : PipeNetworkState.ComputeGasPressure(State.Volume, State.MaxVolume);
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
    float maxVolume = Nodes.Count * PpexValues.LitresPerPipe;
    float frag = Math.Min(
      origPipe.State.Volume / origCount * Nodes.Count,
      maxVolume
    );

    if (frag <= 0f)
    {
      State = null;
      return;
    }

    bool liquid = origPipe.State.IsLiquid;
    State = new PipeNetworkState
    {
      MaxVolume = maxVolume,
      Volume = frag,
      Temperature = origPipe.State.Temperature,
      MediumType = origPipe.State.MediumType,
      Pressure = liquid
        ? origPipe.State.Pressure
        : PipeNetworkState.ComputeGasPressure(frag, maxVolume),
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
    float instantFlow = Math.Max(_producedAccum, _consumedAccum);
    _producedAccum = 0f;
    _consumedAccum = 0f;

    // Refresh the weakest-pipe rating once per tick so chunk load/unload changes are
    // picked up; producer calls between ticks then reuse it at O(1).
    _minBurstCache = null;

    if (State == null)
      return;

    // Smooth the displayed throughput and track how long the run has been genuinely idle.
    _smoothedFlow += (instantFlow - _smoothedFlow) * FlowSmoothingAlpha;
    if (_smoothedFlow < 0.01f)
      _smoothedFlow = 0f;
    if (instantFlow > 0.01f)
      _secondsSinceFlow = 0f;
    else
      _secondsSinceFlow += dt;

    bool changed = false;
    bool liquid = State.IsLiquid;

    State.MaxVolume = Nodes.Count * PpexValues.LitresPerPipe;
    // Gas pressure is the volume ratio; a liquid's pressure is pump-set, so leave it.
    if (!liquid)
    {
      float newPressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );
      if (Math.Abs(State.Pressure - newPressure) > 0.02f)
      {
        State.Pressure = newPressure;
        changed = true;
      }
    }

    if (Math.Abs(State.FlowRate - _smoothedFlow) > 0.01f)
    {
      State.FlowRate = _smoothedFlow;
      changed = true;
    }

    // Particle density for any open-end leaks this tick, scaled by the network-total leak
    // rate (NOT the opening count): gas wisps ramp over 1→8 L/s, water spray over 1→5 L/s.
    float gasLeakRate = Math.Min(
      Math.Max(0f, State.Volume - State.MaxVolume),
      PpexValues.GasLeakRate
    );
    float gasLeakFrac = Math.Clamp(
      (gasLeakRate - 1f) / (PpexValues.GasLeakRate - 1f),
      0f,
      4f
    );
    float waterLeakFrac = Math.Clamp((State.Volume - 1f) / 4f, 0f, 1f);

    // Single pass: detect open connectors and classify each — a vanilla chimney capping
    // the TOP connector of a passthrough / passthrough-bend / outlet is a gas vent (not a
    // leak); an air-exposed end is a leak (gas bleeds, and liquid sprays out as a splash);
    // count gas consumers too.
    int consumers = 0;
    int totalLeaks = 0;
    var chimneyVents = new List<BlockPos>();
    foreach (var pos in Nodes)
    {
      var be = blockAccessor.GetBlockEntity(pos);
      if (be is IPipeNode)
        consumers++;

      if (blockAccessor.GetBlock(pos) is not BlockNetworkNode node)
        continue;

      // Only the dedicated vertical terminations draw through a chimney on their top.
      bool chimneyCapable = node is BlockPipePassthrough or BlockPipeOutlet;

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
        BlockFacing face = openFaces[i];
        BlockPos nPos = pos.AddCopy(face);
        Block neighbour = blockAccessor.GetBlock(nPos);
        // A chimney sitting on the open TOP connector draws gas from the network (no
        // leak). Matched by code so it works for vanilla chimneys and any mod's variant.
        if (
          chimneyCapable
          && face == BlockFacing.UP
          && neighbour.Code?.Path.Contains("chimney") == true
        )
        {
          chimneyVents.Add(nPos);
          continue;
        }
        if (neighbour.FirstCodePart() == "air")
          openFaces[airOpen++] = face;
      }
      if (airOpen == 0)
        continue;

      totalLeaks += airOpen;

      BlockFacing[] leakFaces =
        airOpen == openFaces.Length ? openFaces : openFaces[..airOpen];
      if (be is BlockEntityPipe pipeBe)
      {
        if (State.Volume > 0)
        {
          // Water sprays out of the open end like a poured bucket; gas wisps out.
          if (liquid)
            pipeBe.SpawnLiquidLeak(leakFaces, waterLeakFrac);
          else
            pipeBe.SpawnGasLeak(leakFaces, gasLeakFrac);
        }
      }
      else if (be is INetworkNode nodeEntity && State.Volume > 0)
        nodeEntity.OnOpenConnectorsChanged(leakFaces);
    }

    if (State.OpeningsCount != totalLeaks)
    {
      State.OpeningsCount = totalLeaks;
      changed = true;
    }

    // Chimney draw (gas only) — ChimneyGasDrawRate L/s per chimney-capped top
    // connector. Each drawing chimney puffs smoke so the venting is visible.
    if (!liquid && chimneyVents.Count > 0 && State.Volume > 0)
    {
      float vented = Math.Min(
        State.Volume,
        chimneyVents.Count * PpexValues.ChimneyGasDrawRate
      );
      State.Volume -= vented;
      _consumedAccum += vented;
      changed = true;

      foreach (BlockPos chimneyPos in chimneyVents)
      {
        SpawnChimneySmoke(manager, chimneyPos, State.MediumType);
        // A continuous low fire roar marks the chimney pulling the network's draught.
        if (manager.ServerWorld is { } w)
        {
          long last = _chimneyFireMs.GetValueOrDefault(chimneyPos);
          ExSounds.PlayLoop(
            w,
            chimneyPos,
            ExSounds.Fire,
            ref last,
            ChimneyFireLoopMs,
            volume: 0.3f,
            range: 20f
          );
          _chimneyFireMs[chimneyPos] = last;
        }
      }
    }

    // Drop sound-throttle stamps for chimneys no longer venting this network, so the
    // map can't grow without bound as chimneys are added and removed over a long uptime.
    if (_chimneyFireMs.Count > chimneyVents.Count)
    {
      List<BlockPos>? stale = null;
      foreach (var key in _chimneyFireMs.Keys)
        if (!chimneyVents.Contains(key))
          (stale ??= []).Add(key);
      if (stale != null)
        foreach (var key in stale)
          _chimneyFireMs.Remove(key);
    }

    // Leak loss — a gas leak is a pressure relief (it only bleeds the volume above 1 atm,
    // at a small FIXED rate no matter how many open ends, so a run needs a chimney/stack to
    // vent in bulk); a water leak drains at a fixed rate per tick.
    if (totalLeaks > 0 && State.Volume > 0f)
    {
      if (liquid)
      {
        float lost = Math.Min(State.Volume, PpexValues.LiquidLeakRate * dt);
        State.Volume -= lost;
        if (State.Volume <= 0f)
          State.Pressure = 0f;
      }
      else
      {
        float lost = Math.Min(State.Volume, PpexValues.GasLeakRate);
        State.Volume -= lost;
        if (State.Temperature > 20f)
          State.Temperature = Math.Max(20f, State.Temperature - 5.0f);
      }
      changed = true;
    }

    // Natural evaporation of a water run (in-game time based; same rate as the boiler).
    // Measured off the calendar so it is independent of tick cadence and charges nothing
    // for time the network spent unloaded.
    double nowDays = manager.ServerWorld?.Calendar?.TotalDays ?? -1;
    if (nowDays >= 0)
    {
      if (liquid && _lastEvapDays >= 0 && State.Volume > 0f)
      {
        float evap = (float)(
          PpexValues.EvaporationLitresPerDay * (nowDays - _lastEvapDays)
        );
        if (evap > 0f)
        {
          State.Volume = Math.Max(0f, State.Volume - evap);
          if (State.Volume <= 0f)
            State.Pressure = 0f;
          changed = true;
        }
      }
      _lastEvapDays = nowDays;
    }

    // Keep the broadcast gas pressure in step after venting / leaking.
    if (!liquid && (chimneyVents.Count > 0 || totalLeaks > 0))
      State.Pressure = PipeNetworkState.ComputeGasPressure(
        State.Volume,
        State.MaxVolume
      );

    // Passive cooling of an idle gas run (no consumers drawing it).
    if (
      !liquid
      && State.Volume > 0
      && State.Temperature > 20f
      && consumers == 0
    )
    {
      State.Temperature = Math.Max(20f, State.Temperature - 2.0f);
      changed = true;
    }

    // Clear empty state — only once the run is drained AND has carried no real flow for a
    // few seconds, so a push-and-drain water line (which sits near 0 L while busy) keeps its
    // "Water" label instead of flickering to "empty" between draws.
    if (State.Volume <= 0 && _secondsSinceFlow >= EmptyClearDelaySeconds)
    {
      State = null;
      _smoothedFlow = 0f;
      changed = true;
    }

    if (changed)
      BroadcastUpdate(blockAccessor);

    // Over-pressure failure timer. Production is capped at the weakest pipe's burst
    // rating and leaks vent anything over 1 atm, so a sealed, over-fed run sits exactly
    // at its burst pressure with nowhere to go. Hold there for PipeOverpressureSeconds
    // and a pipe lets go; any relief (a consumer drawing it down, a new leak) that drops
    // the pressure below the rating resets the grace.
    bool pressureFailure = false;
    if (State != null)
    {
      float minBurst = MinBurstPressure(blockAccessor);
      bool overPressure =
        !State.IsLiquid
        && State.Volume > 0f
        && minBurst < float.MaxValue
        && State.Pressure >= minBurst - 0.001f;

      if (overPressure)
      {
        _overpressureSeconds += dt;
        if (_overpressureSeconds >= PpexValues.PipeOverpressureSeconds)
        {
          pressureFailure = true;
          _overpressureSeconds = 0f;
        }
      }
      else if (_overpressureSeconds > 0f)
        _overpressureSeconds = 0f;
    }

    // Over-pressure failures — collected above, executed last so we never mutate the
    // node set while reading it. Each burst removes a node (fracturing the run) and
    // drops the pipe's materials.
    if (State != null && pressureFailure)
    {
      foreach (var pos in CollectBursts(blockAccessor))
        ExecuteBurst(pos, blockAccessor, manager);
    }
  }

  // The weakest-pipe rating is consulted by EVERY TryProduceGas call (each producer,
  // each tick), so walking all nodes per call scaled O(producers × nodes) per second.
  // Cache it; the value only changes when the node set changes (invalidated via
  // OnTopologyChanged) — plus a once-per-tick refresh in OnTick so pipes in chunks
  // that load/unload mid-session are picked up within a second.
  private float? _minBurstCache;

  /// <summary>Drops topology-derived caches when the manager changes the node set.</summary>
  public override void OnTopologyChanged()
  {
    _minBurstCache = null;
  }

  /// <summary>The weakest pipe's burst pressure (atm) across the whole run — the rating
  /// that caps how far the gas pool can be pressurised. <see cref="float.MaxValue"/> when
  /// the run holds no pipes (e.g. only machine ports).</summary>
  private float MinBurstPressure(IBlockAccessor world) =>
    _minBurstCache ??= ComputeMinBurstPressure(world);

  private float ComputeMinBurstPressure(IBlockAccessor world)
  {
    float minBurst = float.MaxValue;
    foreach (var pos in Nodes)
      if (world.GetBlock(pos) is BlockPipe p)
        minBurst = Math.Min(minBurst, p.BurstPressure);
    return minBurst;
  }

  private static readonly Random _rand = new();

  /// <summary>
  /// Finds the pipe that should fail this tick: one random pipe that has held its burst
  /// pressure past the over-pressure grace. Called only when a pressure failure is due.
  /// </summary>
  private List<BlockPos> CollectBursts(IBlockAccessor world)
  {
    var result = new List<BlockPos>();
    if (State == null)
      return result;

    var pressureCandidates = new List<BlockPos>();

    foreach (var pos in Nodes)
    {
      if (world.GetBlock(pos) is not BlockPipe pipe)
        continue;

      if (State.Pressure >= pipe.BurstPressure - 0.001f)
        pressureCandidates.Add(pos);
    }

    if (pressureCandidates.Count > 0)
      result.Add(pressureCandidates[_rand.Next(pressureCandidates.Count)]);

    return result;
  }

  /// <summary>
  /// Breaks a failed pipe: drops its materials, removes it from the graph, and sets the
  /// cell to air. The graph removal handles the network fracture.
  /// </summary>
  private static void ExecuteBurst(
    BlockPos pos,
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
          sworld.SpawnItemEntity(ds, pos.ToVec3d().Add(0.5, 0.5, 0.5));
      }

      // A bursting pipe vents a puff of vapour and pops with a muffled small explosion
      // (server-spawned, so it broadcasts to nearby clients).
      ExParticles.SteamPlume(sworld, pos, 18);
      ExSounds.PlayLocal(sworld, pos, ExSounds.SmallExplosion, 0.4f, 24f);
    }

    manager.RemoveNode(world, pos);
    world.SetBlock(0, pos);
  }

  /// <summary>
  /// Puffs smoke out of a chimney that is actively drawing gas from the network, so the
  /// venting reads visually. The plume is dark and sooty for combustion exhaust, white
  /// for pressurised air or steam vapour. Spawned server-side (broadcasts to clients).
  /// </summary>
  private static void SpawnChimneySmoke(
    BlockNetworkModSystem manager,
    BlockPos chimneyPos,
    string gasType
  )
  {
    if (manager.ServerWorld is { } world)
      ExParticles.ChimneySmoke(world, chimneyPos, gasType);
  }

  #endregion
}

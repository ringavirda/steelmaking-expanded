using System;
using System.Text;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// Block entity for the pressure-relief valve — a <em>directional</em> overflow. It
/// reads the network on its input face (the first letter of the orientation; north in
/// the default "ns", flipped to south when wrenched to "sn") and, whenever that
/// network's gas pressure exceeds the valve's player-set <em>gate pressure</em>, spills
/// the excess (<c>CurrentVolume − gate · MaxVolume</c>) into the network on its output
/// face. The liquid pool spills the same way once its pump-set feed pressure tops the
/// gate. If the output face has no network at all it is treated as an open end: the overflow
/// vents to atmosphere capped like a pipe leak (gas <see cref="PpexValues.GasLeakRate"/>,
/// liquid <see cref="PpexValues.LiquidLeakRate"/>) and sprays particles out
/// the open face. The gate defaults to 1 atm and can be dialled in 0.5 atm steps from
/// <see cref="MinGatePressure"/> up to the valve's own material rating
/// (iron 5 / steel 10 atm).
/// </summary>
[EntityRegister]
public class BlockEntityPressureValve : BlockEntityPipe
{
  /// <summary>Lowest gate pressure the valve can be dialled to (atm, gauge).</summary>
  public const float MinGatePressure = 0f;

  /// <summary>Amount each interaction raises or lowers the gate pressure (atm).</summary>
  public const float GatePressureStep = 0.25f;

  private long _tickId;
  private float _lastVentVolume;
  private float _gatePressure = 1f;

  /// <summary>Pressure (atm, gauge) above which this valve starts venting.</summary>
  public float GatePressure => _gatePressure;

  /// <summary>The valve's material rating — the highest the gate may be set to.</summary>
  public float MaxGatePressure =>
    Block is BlockPressureValve v ? v.BurstPressure : 0f;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    // Clamp against the (possibly reconfigured) material rating on load.
    _gatePressure = GameMath.Clamp(
      _gatePressure,
      MinGatePressure,
      MaxGatePressure
    );
    if (api.Side == EnumAppSide.Server)
      _tickId = RegisterGameTickListener(OnTick, 1000);
  }

  /// <summary>
  /// Steps the gate pressure up or down by <see cref="GatePressureStep"/>, clamped to
  /// [<see cref="MinGatePressure"/>, <see cref="MaxGatePressure"/>]. Returns whether the
  /// value actually changed. Server-side; persists and syncs on change.
  /// </summary>
  public bool AdjustGatePressure(bool increase)
  {
    float delta = increase ? GatePressureStep : -GatePressureStep;
    float next = GameMath.Clamp(
      _gatePressure + delta,
      MinGatePressure,
      MaxGatePressure
    );
    if (Math.Abs(next - _gatePressure) < 0.001f)
      return false;
    _gatePressure = next;
    MarkDirty(true);
    return true;
  }

  private void OnTick(float dt)
  {
    if (
      Block is not BlockPressureValve valve
      || string.IsNullOrEmpty(valve.Orientation)
      || valve.Orientation.Length < 2
    )
      return;

    // Directional: gas/liquid flows from the input face (orientation[0]) to the output
    // face (orientation[1]); a wrench flip swaps "ns" ↔ "sn" to reverse the direction.
    BlockFacing inFace = BlockFacing.FromFirstLetter(valve.Orientation[0]);
    BlockFacing outFace = BlockFacing.FromFirstLetter(valve.Orientation[1]);

    // Only act on networks whose pipe actually presents a connector back at the valve's
    // input/output face — a pipe merely sitting in the adjacent cell with its connectors
    // pointing elsewhere is not plumbed into this valve.
    var ba = Api.World.BlockAccessor;
    var inNet =
      NetworkSystem?.GetConnectedNetworkAcross(ba, Pos, inFace) as PipeNetwork;
    var outNet =
      NetworkSystem?.GetConnectedNetworkAcross(ba, Pos, outFace) as PipeNetwork;

    float moved =
      OverflowGas(inNet, outNet, outFace)
      + OverflowLiquid(inNet, outNet, outFace);

    if (Math.Abs(_lastVentVolume - moved) > 0.01f)
      MarkDirty(true);
    _lastVentVolume = moved;
  }

  /// <summary>
  /// Spills the input network's gas above the gate pressure into the output network. With
  /// no output network the open face vents to atmosphere capped at the pipe-leak rate
  /// (and puffs vapour/exhaust particles). Returns the litres actually moved.
  /// </summary>
  private float OverflowGas(
    PipeNetwork? inNet,
    PipeNetwork? outNet,
    BlockFacing outFace
  )
  {
    var inState = inNet?.State;
    if (inState == null || inState.IsLiquid || inState.MaxVolume <= 0f)
      return 0f;

    float allowed = _gatePressure * inState.MaxVolume;
    if (inState.Volume <= allowed)
      return 0f;

    // Don't push gas into a run that carries water — the receiver would reject it.
    if (outNet?.State is { } os && os.IsLiquid)
      return 0f;

    float excess = inState.Volume - allowed;
    var ba = Api.World.BlockAccessor;
    float temp = inState.Temperature;
    string gasType = inState.MediumType;

    // If there is any output network, push the overflow into it so it is dealt with there.
    // A sealed run (a chimney/stack drains it) can take the whole excess up to its burst
    // ceiling. A bare open-ended run can't be pressurised past 1 atm, so we feed it only the
    // trickle its open ends can shed (GasLeakRatePerOpening) and lift the 1-atm cap for
    // exactly that much — the gas flows in and leaks back out the far end at the open-end rate
    // rather than backing up, and the run never climbs toward a burst. A stack is still needed
    // to vent a run in bulk.
    if (outNet != null)
    {
      // A never-charged output run has a null State (PipeNetwork creates it lazily on
      // first production), so branch on the network itself — not on State — or a fresh
      // output run is mistaken for an open end and the overflow vents to atmosphere
      // instead of filling the run. TryProduceGas creates the State on demand.
      bool leaking = outNet.State?.IsLeaking ?? false;
      float push = leaking ? Math.Min(excess, PpexValues.GasLeakRate) : excess;
      float accepted = outNet.ProduceGasMeasured(
        push,
        temp,
        gasType,
        ba,
        maxOutputPressure: float.MaxValue,
        bypassLeakCap: leaking
      );
      if (accepted > 0f)
        inNet!.TryConsumeGas(accepted, ba);
      return accepted;
    }

    // No output network at all — the valve face is itself an open end, so vent to atmosphere
    // at the small fixed open-end leak rate (a chimney is needed to vent a run in bulk).
    float vented = inNet!.TryConsumeGas(
      Math.Min(excess, PpexValues.GasLeakRate),
      ba
    );
    if (vented > 0f)
    {
      ExParticles.GasVent(Api.World, Pos, outFace, gasType);
      // Same airy swoosh a normal pipe's open end makes when it leaks gas.
      ExSounds.PlayAt(
        Api.World,
        Pos,
        ExSounds.Swoosh,
        range: 24f,
        volume: 0.6f
      );
    }
    return vented;
  }

  /// <summary>
  /// Spills the input network's water into the output network once the pump-set feed
  /// pressure tops the gate. With no output network the open face sprays water out,
  /// capped at the pipe-leak rate. Returns the litres actually moved.
  /// </summary>
  private float OverflowLiquid(
    PipeNetwork? inNet,
    PipeNetwork? outNet,
    BlockFacing outFace
  )
  {
    var inState = inNet?.State;
    if (
      inState == null
      || !inState.IsLiquid
      || inState.Volume <= 0f
      || inState.Pressure <= _gatePressure
    )
      return 0f;

    var ba = Api.World.BlockAccessor;
    float temp = inState.Temperature;
    float press = inState.Pressure;

    // Don't draw water for a run that carries gas — it can't be deposited and would be lost.
    if (outNet?.State is { } os && !os.IsLiquid && os.MediumType.Length > 0)
      return 0f;

    if (outNet != null)
    {
      float free =
        outNet.Nodes.Count * PpexValues.LitresPerPipe
        - (outNet.State?.Volume ?? 0f);
      float move = Math.Min(inState.Volume, free);
      if (move <= 0f)
        return 0f;
      float drawn = inNet!.TryConsumeLiquid(move, ba);
      if (drawn > 0f)
        outNet.TryProduceLiquid(drawn, temp, press, ba);
      return drawn;
    }

    float spilled = inNet!.TryConsumeLiquid(
      Math.Min(inState.Volume, PpexValues.LiquidLeakRate),
      ba
    );
    if (spilled > 0f)
    {
      ExParticles.WaterJet(Api.World, Pos, outFace);
      ExSounds.SplashSound(Api.World, Pos);
    }
    return spilled;
  }

  public override void OnBlockRemoved()
  {
    base.OnBlockRemoved();
    if (_tickId != 0)
    {
      UnregisterGameTickListener(_tickId);
      _tickId = 0;
    }
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc)
  {
    dsc.AppendLine(
      Lang.Get(
        "ppex:gaspressurevalve-info-rating",
        _gatePressure,
        MaxGatePressure
      )
    );
    if (_lastVentVolume > 0f)
      dsc.AppendLine(
        Lang.Get("ppex:gaspressurevalve-info-overflow", _lastVentVolume)
      );
  }

  public override void ToTreeAttributes(ITreeAttribute tree)
  {
    base.ToTreeAttributes(tree);
    tree.SetFloat("gatePressure", _gatePressure);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  )
  {
    base.FromTreeAttributes(tree, worldForResolving);
    // Pre-existing valves saved before the gate was configurable default to 1 atm.
    _gatePressure = tree.GetFloat("gatePressure", 1f);
  }
}

using System;
using System.Text;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using PipesAndPowerExpanded.Helpers;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;

/// <summary>
/// Block entity for the pressure-relief valve - a <em>directional</em> overflow. It reads the
/// network on its input face (orientation[0]; wrench flips "ns" ↔ "sn") and, whenever its
/// pressure exceeds the player-set gate, spills the excess into the output-face network. Liquid
/// spills the same way once its pump-set feed pressure tops the gate. With no output network the
/// output face is an open end: the overflow vents to atmosphere capped at the pipe-leak rate with
/// particles. The gate defaults to 1 atm, dialled in steps up to the valve's material rating.
/// </summary>
[BlockEntityRegister]
public class BlockEntityPressureValve : BlockEntityPipe {
  /// <summary>Lowest gate pressure the valve can be dialled to (atm, gauge).</summary>
  public const float MinGatePressure = 0f;

  /// <summary>Amount each interaction raises or lowers the gate pressure (atm).</summary>
  public const float GatePressureStep = 0.25f;

  private long _tickId;
  private float _lastVentVolume;
  private float _gatePressure = 1f;

  /// <summary>Pressure (atm, gauge) above which this valve starts venting.</summary>
  public float GatePressure => _gatePressure;

  /// <summary>The valve's material rating - the highest the gate may be set to.</summary>
  public float MaxGatePressure =>
    Block is BlockPressureValve v ? v.BurstPressure : 0f;

  public override void Initialize(ICoreAPI api) {
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
  public bool AdjustGatePressure(bool increase) {
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

  private void OnTick(float dt) {
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

    // Only act on networks whose pipe presents a connector back at the valve's face; one
    // merely sitting adjacent with connectors elsewhere is not plumbed in.
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
  ) {
    var inState = inNet?.State;
    if (inState == null || inState.IsLiquid || inState.MaxVolume <= 0f)
      return 0f;

    float allowed = _gatePressure * inState.MaxVolume;
    if (inState.Volume <= allowed)
      return 0f;

    // Don't push gas into a run that carries water - the receiver would reject it.
    if (outNet?.State is { } os && os.IsLiquid)
      return 0f;

    float excess = inState.Volume - allowed;
    var ba = Api.World.BlockAccessor;
    float temp = inState.Temperature;
    string gasType = inState.MediumType;
    float inPressure = inState.Volume / inState.MaxVolume;

    if (outNet != null) {
      // Branch on the network, not State - a never-charged run has a null State (created
      // lazily on first production) and would be mistaken for an open end.
      bool leaking = outNet.State?.IsLeaking ?? false;

      // A leaking output can't hold pressure, so feed it only the trickle its open ends shed
      // and let that flow straight through (bypassLeakCap lifts the 1-atm cap for exactly that).
      if (leaking) {
        float vent = outNet.ProduceGasMeasured(
          Math.Min(excess, PpexValues.GasLeakRate),
          temp,
          gasType,
          ba,
          maxOutputPressure: float.MaxValue,
          bypassLeakCap: true
        );
        if (vent > 0f)
          inNet!.TryConsumeGas(vent, ba);
        return vent;
      }

      // A pressure-relief valve only flows DOWNHILL: gas may cross only while the output run
      // sits below the input pressure. Otherwise (e.g. the output branch's own pressure has
      // built past the input) it would keep pumping that loop ever higher until its pipes burst.
      float outMax = outNet.Nodes.Count * PpexValues.LitresPerPipe;
      if (outMax <= 0f)
        return 0f;
      float outVol = outNet.State?.Volume ?? 0f;
      float outPressure = outVol / outMax;
      if (outPressure >= inPressure - 0.001f)
        return 0f;

      // Move just enough to equalise the two runs' pressures - the relief settles where they
      // balance - but never more than the excess above the gate (so the input is never drawn
      // below the gate, and the blowers can keep topping it up). Capping the output ceiling at
      // the input pressure double-guards against ever driving the output above the input.
      float equalise =
        (outMax * inState.Volume - inState.MaxVolume * outVol)
        / (inState.MaxVolume + outMax);
      float toMove = Math.Min(excess, equalise);
      if (toMove <= 0f)
        return 0f;

      float accepted = outNet.ProduceGasMeasured(
        toMove,
        temp,
        gasType,
        ba,
        maxOutputPressure: inPressure,
        bypassLeakCap: false
      );
      if (accepted > 0f)
        inNet!.TryConsumeGas(accepted, ba);
      return accepted;
    }

    // No output network - the valve face is an open end, so vent to atmosphere at the fixed
    // open-end leak rate (a chimney is needed to vent in bulk).
    float vented = inNet!.TryConsumeGas(
      Math.Min(excess, PpexValues.GasLeakRate),
      ba
    );
    if (vented > 0f) {
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
  ) {
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

    // Don't draw water for a run that carries gas - it can't be deposited and would be lost.
    if (outNet?.State is { } os && !os.IsLiquid && os.MediumType.Length > 0)
      return 0f;

    if (outNet != null) {
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
    if (spilled > 0f) {
      ExParticles.WaterJet(Api.World, Pos, outFace);
      ExSounds.SplashSound(Api.World, Pos);
    }
    return spilled;
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    if (_tickId != 0) {
      UnregisterGameTickListener(_tickId);
      _tickId = 0;
    }
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    dsc.AppendLine(
      Lang.Get(
        "ppex:gaspressurevalve-info-rating",
        ExMeasure.PressureRange(_gatePressure, MaxGatePressure)
      )
    );
    if (_lastVentVolume > 0f)
      dsc.AppendLine(
        Lang.Get(
          "ppex:gaspressurevalve-info-overflow",
          ExMeasure.Volume(_lastVentVolume)
        )
      );
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetFloat("gatePressure", _gatePressure);
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    // Pre-existing valves saved before the gate was configurable default to 1 atm.
    _gatePressure = tree.GetFloat("gatePressure", 1f);
  }
}

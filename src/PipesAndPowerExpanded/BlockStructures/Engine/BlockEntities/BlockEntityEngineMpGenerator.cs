using System;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Engine sub-machine: a mechanical-power generator. Torque is injected into the vanilla MP
/// network by the <see cref="BEBehaviorEngineMPGenerator"/> behavior (which reads this BE's
/// <see cref="BlockEntityEngineSubmachine.Engine"/> power); the visible motion is its spinning
/// axle. The generator owns no animation of its own, so it <b>drives the engine's</b>
/// <c>cyclemp</c> animation instead: every render frame it pushes its axle angle to the engine
/// (one revolution = one cycle), keeping the two locked at any speed and cycling while the
/// flywheel coasts after the steam is cut.
/// </summary>
[BlockEntityRegister]
public class BlockEntityEngineMpGenerator
  : BlockEntityEngineSubmachine,
    IRenderer {
  private BEBehaviorEngineMPGenerator? _mp;
  private ICoreClientAPI? _capi;

  // Low metal-on-metal grind from the spinning gear train while the axle turns (client only).
  private ILoadedSound? _grindSound;

  // Update the engine's frame before the opaque pass so it renders in step with the axle.
  public double RenderOrder => 0.0;
  public int RenderRange => 64;

  /// <summary>
  /// Full power whenever an engine is attached. An overloaded engine labours rather than switching
  /// off: the network settles at <c>speed = budget / load</c>, so a heavy shaft crawls, and comes to
  /// a stand only past the torque the generator can raise at all - four times the budget, per the
  /// divisor clamp in <see cref="BEBehaviorEngineMPGenerator.GetTorque"/>. The readout below reports
  /// the labouring state; nothing punishes it.
  /// </summary>
  public override float PowerDemand => Engine != null ? 1f : 0f;

  // No pipe work - power leaves as MP torque via the behavior.
  protected override void DoWork(float power, float dt) { }

  protected override string? OutputInfo(float power) {
    if (Engine is not { } engine)
      return null;

    float speed = System.Math.Abs(_mp?.Network?.Speed ?? 0f);
    float load = _mp?.Network?.NetworkResistance ?? 0f;
    string line = Lang.Get(
      "ppex:enginempgenerator-info-driving",
      speed.ToString("0.00"),
      engine.ShaftSpeed.ToString("0.00")
    );

    // The one place the overstress state reaches a player: a shaft loaded past what the engine can
    // hold turns slower than its cap and there is otherwise nothing to say why.
    return engine.IsMpOverstressed(load)
      ? line + " " + Lang.Get("ppex:enginempgenerator-info-labouring")
      : line;
  }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _mp = GetBehavior<BEBehaviorEngineMPGenerator>();
    if (api is ICoreClientAPI capi) {
      _capi = capi;
      capi.Event.RegisterRenderer(
        this,
        EnumRenderStage.Before,
        "ppex-engine-mpcycle"
      );
    }
  }

  /// <summary>
  /// Per render frame: pushes the axle's current render angle to the master engine so it can
  /// lock its cycle animation to the visible axle (see <see cref="BlockEntityEngine.DriveMpCycleFrame"/>).
  /// </summary>
  public void OnRenderFrame(float deltaTime, EnumRenderStage stage) {
    if (_mp == null)
      return;
    bool turning = _mp.Network != null && Math.Abs(_mp.Network.Speed) > 0.001f;
    UpdateGrindSound(turning);
    if (Engine is { } engine)
      // Drive the engine cycle off the axle's signed render angle so its rod tracks the axle's
      // visible spin direction (folding in AxisSign tracked the opposite way - the rendered axle
      // already turns with AngleRad here).
      engine.DriveMpCycleFrame(turning, _mp.AngleRad);
  }

  /// <summary>Runs a quiet looping metal-grind while the axle is turning; stops it when it stalls.</summary>
  private void UpdateGrindSound(bool turning) {
    if (_capi == null)
      return;
    if (turning) {
      _grindSound ??= ExSounds.CreateLoop(
        _capi,
        Pos,
        ExSounds.MetalGrinding,
        0.3f,
        16f,
        0.85f
      );
      if (_grindSound is { IsPlaying: false })
        _grindSound.Start();
    } else if (_grindSound is { IsPlaying: true })
      _grindSound.Stop();
  }

  /// <summary>
  /// Re-applies the axle orientation when the engine snapped this generator to its matching facing;
  /// the base re-resolves the engine while this re-seeds the mechanical axis.
  /// </summary>
  public override void OnExchanged(Block block) {
    base.OnExchanged(block);
    _mp?.OnOrientationChanged();
  }

  public void Dispose() {
    _grindSound?.Stop();
    _grindSound?.Dispose();
    _grindSound = null;
    _capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
  }

  public override void OnBlockRemoved() {
    Dispose();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded() {
    Dispose();
    base.OnBlockUnloaded();
  }
}

using System;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Cornish-engine sub-machine: a mechanical-power generator. The actual torque is
/// injected into the vanilla MP network by the <c>smex.BEBehaviorMPGenerator</c>
/// behavior, which reads this block entity's <see cref="BlockEntityEngineSubmachine.Engine"/>
/// power; the visible motion is the MP behavior's spinning axle (synced to network speed).
/// <para>
/// The generator owns no idle/cycle animation of its own — its motion is the spinning axle.
/// Because the master engine's beam/piston cycle is physically coupled to that axle, this
/// generator <b>drives the engine's cycle animation</b> rather than the other way around: every
/// render frame it pushes its axle's current rotation angle to the engine, which locks its
/// <c>cyclemp</c> animation frame-for-frame to it (one axle revolution = one cycle). This keeps
/// the two in lockstep at any speed, and keeps the engine cycling while the flywheel coasts down
/// after the steam is cut.
/// </para>
/// </summary>
[EntityRegister]
public class BlockEntityEngineMpGenerator
  : BlockEntityEngineSubmachine,
    IRenderer
{
  private BEBehaviorEngineMPGenerator? _mp;
  private ICoreClientAPI? _capi;

  // Low metal-on-metal grind from the spinning gear train while the axle turns (client only).
  private ILoadedSound? _grindSound;

  // Update the engine's frame before the opaque pass so it renders in step with the axle this
  // frame; no GL work of our own, so any range is fine.
  public double RenderOrder => 0.0;
  public int RenderRange => 64;

  // The generator wants full power while an engine is attached and the MP load is within what the
  // engine can drive. Once the load drags the network below half the rated speed the engine is
  // overstressed and cuts out entirely (demand 0 → no power → the line coasts down) until enough
  // machines are removed. Judged by the network's resistance, not the live speed, so a stalled
  // engine still recovers when the load is shed rather than latching off at speed 0.
  public override float PowerDemand
  {
    get
    {
      if (Engine is not { } engine)
        return 0f;
      float load = _mp?.Network?.NetworkResistance ?? 0f;
      return engine.IsMpOverstressed(load) ? 0f : 1f;
    }
  }

  // No pipe work — power leaves as MP torque via the behavior.
  protected override void DoWork(float power, float dt) { }

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    _mp = GetBehavior<BEBehaviorEngineMPGenerator>();
    if (api is ICoreClientAPI capi)
    {
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
  public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
  {
    if (_mp == null)
      return;
    bool turning = _mp.Network != null && Math.Abs(_mp.Network.Speed) > 0.001f;
    UpdateGrindSound(turning);
    if (Engine is { } engine)
      engine.DriveMpCycleFrame(turning, _mp.AngleRad);
  }

  /// <summary>Runs a quiet looping metal-grind while the axle is turning; stops it when it stalls.</summary>
  private void UpdateGrindSound(bool turning)
  {
    if (_capi == null)
      return;
    if (turning)
    {
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
    }
    else if (_grindSound is { IsPlaying: true })
      _grindSound.Stop();
  }

  /// <summary>
  /// Re-applies the axle orientation when the engine snapped this generator to its matching
  /// facing (case 1: engine placed onto an already-present generator); the base re-resolves the
  /// engine while this re-seeds the mechanical axis.
  /// </summary>
  public override void OnExchanged(Block block)
  {
    base.OnExchanged(block);
    _mp?.OnOrientationChanged();
  }

  public void Dispose()
  {
    _grindSound?.Stop();
    _grindSound?.Dispose();
    _grindSound = null;
    _capi?.Event.UnregisterRenderer(this, EnumRenderStage.Before);
  }

  public override void OnBlockRemoved()
  {
    Dispose();
    base.OnBlockRemoved();
  }

  public override void OnBlockUnloaded()
  {
    Dispose();
    base.OnBlockUnloaded();
  }
}

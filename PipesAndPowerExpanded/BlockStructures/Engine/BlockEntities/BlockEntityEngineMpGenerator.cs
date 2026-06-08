using ExpandedLib.EntityRegistry;

namespace PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;

/// <summary>
/// Cornish-engine sub-machine: a mechanical-power generator. The actual torque is
/// injected into the vanilla MP network by the <c>smex.BEBehaviorMPGenerator</c>
/// behavior, which reads this block entity's <see cref="BlockEntityEngineSubmachine.Engine"/>
/// power; the visible motion is the MP behavior's spinning axle (synced to network speed).
/// </summary>
[EntityRegister]
public class BlockEntityEngineMpGenerator : BlockEntityEngineSubmachine
{
  // The generator always wants power while an engine is attached; the MP behavior
  // tapers its own torque as the network speeds up.
  public override float PowerDemand => Engine != null ? 1f : 0f;

  // No pipe work — power leaves as MP torque via the behavior.
  protected override void DoWork(float power, float dt) { }
}

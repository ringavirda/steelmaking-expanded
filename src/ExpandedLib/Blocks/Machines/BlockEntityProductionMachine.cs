using ExpandedLib.Blocks.Networks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks.Machines;

/// <summary>
/// Base for any block entity that does periodic server-side production work, whether or not it is a
/// multiblock (<see cref="ExpandedLib.Blocks.Structures.BlockEntityMultiblockStructure"/> derives
/// from this; so do the standalone steam engines and their sub-machines). It owns the production
/// tick lifecycle - registration, the operational gate, and teardown - so a concrete machine writes
/// only its per-tick logic in <see cref="OnProductionTick"/> plus the gate in
/// <see cref="CanRunProduction"/>, and reads networks through the inherited port helpers.
///
/// The tick runs every <see cref="ProductionTickMs"/> ms on the server. Each tick is gated by
/// <see cref="CanRunProduction"/>: when it returns <c>false</c> the machine is idle and
/// <see cref="OnIdleProductionTick"/> runs instead (default no-op).
/// </summary>
public abstract class BlockEntityProductionMachine : BlockEntity
{
  private long _productionTickId;

  /// <summary>Interval (ms) of the production tick.</summary>
  protected virtual int ProductionTickMs => 1000;

  /// <summary>
  /// Whether the machine is in an operational state this tick (e.g. structure complete, finished
  /// construction). Returning <c>false</c> routes the tick to <see cref="OnIdleProductionTick"/>.
  /// </summary>
  protected abstract bool CanRunProduction { get; }

  /// <summary>
  /// Whether <see cref="Initialize"/> should register the production tick immediately. Default
  /// <c>true</c> (the machine self-gates each tick). A machine that registers/unregisters the tick
  /// on a state change (e.g. a multiblock that only ticks while complete) overrides this and drives
  /// <see cref="StartProductionTick"/>/<see cref="StopProductionTick"/> itself.
  /// </summary>
  protected virtual bool AutoStartProduction => true;

  public override void Initialize(ICoreAPI api)
  {
    base.Initialize(api);
    if (api.Side == EnumAppSide.Server && AutoStartProduction)
      StartProductionTick();
  }

  /// <summary>Registers the production tick (idempotent, server-side only).</summary>
  protected void StartProductionTick()
  {
    if (_productionTickId == 0 && Api?.Side == EnumAppSide.Server)
      _productionTickId = RegisterGameTickListener(
        RunProductionTick,
        ProductionTickMs
      );
  }

  /// <summary>Unregisters the production tick.</summary>
  protected void StopProductionTick()
  {
    if (_productionTickId != 0)
    {
      UnregisterGameTickListener(_productionTickId);
      _productionTickId = 0;
    }
  }

  /// <summary>
  /// How many of its own intervals a single tick may integrate over. A server hitch, a rejoin or a
  /// slow chunk load hands the listener the whole stalled interval as one dt; machines integrate over
  /// it (over-pressure grace timers most of all), so an unclamped step can cross a burst threshold in
  /// one tick and destroy a machine that was never actually over pressure. Catching up slowly is the
  /// lesser evil - the alternative is a boiler that explodes because the server lagged.
  /// </summary>
  private const float MaxCatchupTickMultiple = 2f;

  private void RunProductionTick(float dt)
  {
    dt = GameMath.Min(dt, ProductionTickMs / 1000f * MaxCatchupTickMultiple);
    if (CanRunProduction)
      OnProductionTick(dt);
    else
      OnIdleProductionTick(dt);
  }

  /// <summary>Per-tick production logic; runs server-side only while <see cref="CanRunProduction"/>.</summary>
  protected abstract void OnProductionTick(float dt);

  /// <summary>Runs in place of <see cref="OnProductionTick"/> while the machine is not operational. Default: no-op.</summary>
  protected virtual void OnIdleProductionTick(float dt) { }

  public override void OnBlockRemoved()
  {
    base.OnBlockRemoved();
    StopProductionTick();
  }

  public override void OnBlockUnloaded()
  {
    base.OnBlockUnloaded();
    StopProductionTick();
  }

  #region Network ports

  // NOTE: these forward to the MachinePorts extension methods by their fully-qualified static form.
  // Writing `this.ConnectedNetwork(...)` would bind to THIS instance method (instance methods shadow
  // extension methods), recursing forever - so the static call is required, not stylistic.

  /// <summary>The <typeparamref name="TNet"/> across <paramref name="face"/>, or <c>null</c> if not plumbed in.</summary>
  protected TNet? ConnectedNetwork<TNet>(BlockFacing face)
    where TNet : BlockNetwork =>
    MachinePorts.ConnectedNetwork<TNet>(this, face);

  /// <summary>The <typeparamref name="TNet"/> owning <paramref name="pos"/>, or <c>null</c>.</summary>
  protected TNet? NetworkAt<TNet>(BlockPos pos)
    where TNet : BlockNetwork => MachinePorts.NetworkAt<TNet>(this, pos);

  #endregion
}

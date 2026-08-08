using ExpandedLib.Blocks.Machines;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A concrete production machine for driving the base tick lifecycle in tests.</summary>
internal sealed class TestProductionMachine : BlockEntityProductionMachine
{
  public bool Operational = true;
  public int ProductionTicks;
  public int IdleTicks;

  /// <summary>The dt the last tick actually received, to assert the catch-up clamp.</summary>
  public float LastDt;

  protected override bool CanRunProduction => Operational;

  protected override void OnProductionTick(float dt)
  {
    ProductionTicks++;
    LastDt = dt;
  }

  protected override void OnIdleProductionTick(float dt)
  {
    IdleTicks++;
    LastDt = dt;
  }

  /// <summary>Exposes the protected registration so a test can start ticking without full Initialize.</summary>
  public void StartTicking() => StartProductionTick();
}

/// <summary>The shared production-tick template: it gates each tick on <c>CanRunProduction</c>.</summary>
public class ProductionMachineTests
{
  private static (TestWorld world, TestProductionMachine machine) NewMachine()
  {
    var world = new TestWorld();
    var machine = new TestProductionMachine { Pos = new BlockPos(0, 0, 0) };
    world.Attach(machine);
    machine.StartTicking();
    return (world, machine);
  }

  #region Tick gating

  [Fact]
  public void Runs_production_each_tick_while_operational()
  {
    var (world, machine) = NewMachine();

    world.FireBlockEntityTicks(times: 3);

    Assert.Equal(3, machine.ProductionTicks);
    Assert.Equal(0, machine.IdleTicks);
  }

  [Fact]
  public void Routes_to_idle_while_not_operational()
  {
    var (world, machine) = NewMachine();
    machine.Operational = false;

    world.FireBlockEntityTicks(times: 2);

    Assert.Equal(0, machine.ProductionTicks);
    Assert.Equal(2, machine.IdleTicks);
  }

  [Fact]
  public void Resumes_production_when_it_becomes_operational_again()
  {
    var (world, machine) = NewMachine();

    machine.Operational = false;
    world.FireBlockEntityTicks();
    machine.Operational = true;
    world.FireBlockEntityTicks();

    Assert.Equal(1, machine.ProductionTicks);
    Assert.Equal(1, machine.IdleTicks);
  }

  #endregion

  #region Catch-up clamp

  // A server hitch (or a rejoin) delivers the whole stalled interval as one oversized dt. Machines
  // integrate over dt - over-pressure grace timers most of all - so an unclamped catch-up step lets a
  // boiler cross its burst threshold in a single tick and explode on a setup that was never over
  // pressure. The tick is capped at a small multiple of its own interval instead.

  [Fact]
  public void Clamps_an_oversized_catch_up_dt_before_the_production_tick()
  {
    var (world, machine) = NewMachine();

    world.FireBlockEntityTicks(dt: 60f);

    Assert.Equal(1, machine.ProductionTicks);
    Assert.True(
      machine.LastDt <= 2f,
      $"production tick saw dt={machine.LastDt}s; a 60s catch-up must be clamped"
    );
  }

  [Fact]
  public void Clamps_an_oversized_catch_up_dt_before_the_idle_tick()
  {
    var (world, machine) = NewMachine();
    machine.Operational = false;

    world.FireBlockEntityTicks(dt: 60f);

    Assert.Equal(1, machine.IdleTicks);
    Assert.True(
      machine.LastDt <= 2f,
      $"idle tick saw dt={machine.LastDt}s; a 60s catch-up must be clamped"
    );
  }

  // Control: the clamp must not distort an ordinary tick, or every rate in every machine shifts.
  [Fact]
  public void Passes_an_ordinary_dt_through_unchanged()
  {
    var (world, machine) = NewMachine();

    world.FireBlockEntityTicks(dt: 1f);

    Assert.Equal(1f, machine.LastDt);
  }

  #endregion
}

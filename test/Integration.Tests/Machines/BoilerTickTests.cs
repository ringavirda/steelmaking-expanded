using ExpandedLib.Testing;
using Xunit;
using BoilerState = PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntityBoiler.BoilerState;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The boiler's full production tick - the construction/structure-gated state machine that the
/// math/serialization tests could not reach. Driven through <see cref="BoilerRig"/>, which fakes the
/// finished construction, the structure, and a burning firebox.
/// </summary>
public class BoilerTickTests {
  [Fact]
  public void Rig_reports_a_constructed_operational_boiler() {
    var rig = new BoilerRig();
    Assert.True(rig.Be.IsConstructed);
    Assert.True(rig.Be.IsOperational);
  }

  [Fact]
  public void Idle_starts_heating_when_fired_with_enough_water() {
    var rig = new BoilerRig().SetState(BoilerState.Idle).SetWater(200f);

    rig.Tick();

    Assert.Equal(BoilerState.Heating, rig.State);
  }

  [Fact]
  public void Idle_stays_idle_without_enough_water() {
    // The Cornish boiler needs 150 L; 100 L is below the floor.
    var rig = new BoilerRig().SetState(BoilerState.Idle).SetWater(100f);

    rig.Tick();

    Assert.Equal(BoilerState.Idle, rig.State);
  }

  [Fact]
  public void Idle_stays_idle_when_the_fire_is_out() {
    var rig = new BoilerRig()
      .SetState(BoilerState.Idle)
      .SetWater(200f)
      .ExtinguishFire();

    rig.Tick();

    Assert.Equal(BoilerState.Idle, rig.State);
  }

  [Fact]
  public void Heating_reaches_boiling_once_the_heat_up_time_elapses() {
    var rig = new BoilerRig()
      .SetState(BoilerState.Heating)
      .SetWater(200f)
      .SetHeatingSeconds(PpexValues.BoilerHeatUpSeconds - 1f);

    rig.Tick(); // crosses the heat-up threshold this tick

    Assert.Equal(BoilerState.Boiling, rig.State);
  }

  [Fact]
  public void Boiling_converts_water_into_steam() {
    var rig = new BoilerRig()
      .SetState(BoilerState.Boiling)
      .SetWater(200f)
      .SetSteam(0f);

    rig.Tick();

    Assert.True(rig.SteamVolume > 0f, "boiling should generate steam");
    // BoilStep consumes SteamPerSecond/expansion litres of water per second (32/16 = 2 L for the
    // Cornish). The steam pool itself isn't asserted exactly here: with no pipe on the outlet the
    // open neck leaks some of it back out the same tick (covered by the leak path elsewhere).
    float expectedWaterUse =
      PpexValues.CornishBoilerSteamPerSecond / PpexValues.SteamExpansionFactor;
    Assert.Equal(200f - expectedWaterUse, rig.WaterVolume, 2);
  }

  [Fact]
  public void A_running_boiler_shuts_down_after_the_grace_period_when_the_fire_dies() {
    var rig = new BoilerRig()
      .SetState(BoilerState.Boiling)
      .SetWater(200f)
      .SetShutdownSeconds(PpexValues.BoilerShutdownDelaySeconds)
      .ExtinguishFire();

    rig.Tick(); // pushes the shutdown timer past the grace period

    Assert.Equal(BoilerState.Idle, rig.State);
  }

  [Fact]
  public void Heating_aborts_back_to_idle_after_grace_when_water_runs_out() {
    var rig = new BoilerRig()
      .SetState(BoilerState.Heating)
      .SetWater(0f) // below the boil floor
      .SetShutdownSeconds(PpexValues.BoilerShutdownDelaySeconds);

    rig.Tick();

    Assert.Equal(BoilerState.Idle, rig.State);
  }

  // Re-use regression (the cowper lesson generalized): a boiler is fired, shut down, and re-fired
  // repeatedly. Shutdown deliberately LEAVES leftover steam in the vessel (it condenses back to water
  // in Idle), so a re-fire happens against a dirty, steam-laden vessel. That residual steam must not
  // latch the boiler out of operation - re-lighting with enough water must reach Heating again. Every
  // other boiler test runs a single forward leg from primed state; none re-fires after a shutdown.
  [Fact]
  public void A_boiler_re_fires_after_a_shutdown_despite_leftover_steam() {
    // Run to Boiling, snuff the fire, and tick past the grace so it shuts down to Idle - with steam
    // still in the vessel.
    var rig = new BoilerRig()
      .SetState(BoilerState.Boiling)
      .SetWater(300f)
      .SetSteam(200f)
      .SetShutdownSeconds(PpexValues.BoilerShutdownDelaySeconds)
      .ExtinguishFire();
    rig.Tick();
    Assert.Equal(BoilerState.Idle, rig.State);
    Assert.True(
      rig.SteamVolume > 0f,
      "precondition: leftover steam remains in the vessel after shutdown"
    );

    // Re-light and top the water back up: the second heat must reach Heating again - the residual
    // steam must not block the re-fire.
    rig.RelightFire().SetWater(300f).Tick();

    Assert.Equal(BoilerState.Heating, rig.State);
  }
}

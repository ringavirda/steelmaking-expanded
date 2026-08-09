using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The MP-generator sub-machine's mechanical-power behavior - the mod's first torque source, a
/// constant-power model that had 0% coverage. Drives the engine→generator half of the MP chain: the
/// generator turns the engine's <see cref="BlockEntityEngine.MpPowerBudget"/> into network torque
/// (rated speed at rated load, more torque under heavier load, tapering to zero past a soft speed
/// cap), and the generator cuts its power demand when the load overstresses the engine.
/// </summary>
public class MpGeneratorBehaviorTests {
  private static (
    Scene scene,
    MpGeneratorPlant plant,
    BEBehaviorEngineMPGenerator mp
  ) Rig(float availablePower) {
    var scene = new Scene().Network("pipe", s => new PipeNetwork(s));
    var plant = new MpGeneratorPlant(scene, new BlockPos(0, 8, 0));
    scene.Build();
    // Set the engine's available power directly (deterministic) instead of driving steam.
    ReflectionHelpers.SetField(
      plant.Engine,
      "<AvailablePower>k__BackingField",
      availablePower
    );
    var mp = new BEBehaviorEngineMPGenerator(plant.Generator);
    return (scene, plant, mp);
  }

  #region Torque curve

  [Fact]
  public void Resistance_is_the_constant_rotor_drag() {
    var (_, _, mp) = Rig(0.5f);
    Assert.Equal(0.0005f, mp.GetResistance(), 5);
  }

  [Fact]
  public void Torque_at_rated_speed_equals_the_engines_power_budget() {
    var (_, plant, mp) = Rig(0.5f);
    float budget = plant.Engine.MpPowerBudget;
    Assert.True(budget > 0f, "the engine should have a power budget");

    float torque = mp.GetTorque(
      0,
      PpexValues.MpRatedSpeed,
      out float resistance
    );

    Assert.Equal(budget, torque, 3);
    Assert.Equal(0f, resistance, 5);
  }

  [Fact]
  public void It_is_a_constant_power_source_more_torque_at_lower_speed() {
    var (_, plant, mp) = Rig(0.5f);
    float budget = plant.Engine.MpPowerBudget;
    float rated = PpexValues.MpRatedSpeed;

    float half = mp.GetTorque(0, 0.5f * rated, out _);

    Assert.True(
      half > budget,
      "torque should rise as the shaft slows (constant power)"
    );
    Assert.Equal(budget, half * 0.5f * rated, 3); // power = torque × speed is held at the budget
  }

  [Fact]
  public void Torque_tapers_to_zero_past_the_soft_speed_cap() {
    var (_, _, mp) = Rig(0.5f);
    float rated = PpexValues.MpRatedSpeed;

    Assert.Equal(0f, mp.GetTorque(0, 1.5f * rated, out _), 4); // at the cap end
    Assert.Equal(0f, mp.GetTorque(0, 2f * rated, out _), 4); // beyond it
  }

  [Fact]
  public void Without_steam_the_generator_makes_no_torque() {
    var (_, _, mp) = Rig(0f); // no available power
    Assert.Equal(0f, mp.GetTorque(0, PpexValues.MpRatedSpeed, out _), 5);
  }

  #endregion

  #region Orientation

  [Fact]
  public void Orientation_seeds_the_axle_axis_from_the_side_variant() {
    var (_, _, mp) = Rig(0.5f); // the plant's generator block is "east"
    mp.SetOrientations();

    Assert.Equal(BlockFacing.WEST, mp.OutFacingForNetworkDiscovery);
    Assert.Equal(new[] { -1, 0, 0 }, mp.AxisSign); // single sign per axis (X)
  }

  #endregion

  #region Load management (PowerDemand)

  [Fact]
  public void Power_demand_is_full_under_a_normal_load() {
    var (_, plant, mp) = Rig(0.5f);
    MechPower.Attach(
      plant.Generator,
      mp,
      MechPower.Network(speed: 1f, resistance: 0f)
    );
    ReflectionHelpers.SetField(plant.Generator, "_mp", mp);

    Assert.Equal(1f, plant.Generator.PowerDemand, 3);
  }

  [Fact]
  public void Power_demand_cuts_out_when_the_load_overstresses_the_engine() {
    var (_, plant, mp) = Rig(0.5f);
    float overload = 3f * plant.Engine.MpRatedLoad; // past the 2× rated overstress ceiling
    MechPower.Attach(
      plant.Generator,
      mp,
      MechPower.Network(speed: 1f, resistance: overload)
    );
    ReflectionHelpers.SetField(plant.Generator, "_mp", mp);

    Assert.Equal(0f, plant.Generator.PowerDemand, 3); // demand drops so the engine can recover
  }

  #endregion
}

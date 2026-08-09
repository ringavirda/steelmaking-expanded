using ExpandedLib.Testing;
using PipesAndPowerExpanded;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.Tests;
using SteelmakingExpanded.BlockStructures.SmokeStack.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Whole-process hot-blast scenarios (handbook hot-blast article): the smoke stack as the furnace's
/// safety valve - it vents the surplus exhaust the cowper stoves can't swallow, and a backed-up
/// exhaust line chokes the furnace. Models a furnace continuously spilling exhaust into a gas main and
/// asserts the stack keeps the line from running away, where without it the main backs up.
/// </summary>
public class HotBlastScenarioTests {
  private static BlockEntitySmokeStack Stack(TestWorld world, BlockPos pos) {
    var be = new BlockEntitySmokeStack {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:smokestack-north",
        70,
        ("orientation", "north")
      ),
    };
    world.Attach(be);
    ReflectionHelpers.SetField(be, "_system", world.Networks);
    ReflectionHelpers.SetProperty(be, "StructureComplete", true);
    return be;
  }

  /// <summary>One tick of the furnace spilling <paramref name="exhaust"/> litres into the main.</summary>
  private static void Furnace(
    TestWorld world,
    PipeNetwork net,
    float exhaust
  ) =>
    net.TryProduceGas(
      exhaust,
      700f,
      "Exhaust",
      world.Accessor,
      maxOutputPressure: 20f
    );

  #region Smoke stack safety valve

  // Furnace exhaust per tick: under one stack intake (48 L), so a working stack keeps ahead of it,
  // but 12 ticks of it (≈460 L) would swamp the 180 L main if nothing vents.
  private const float FurnacePerTick = 38f;

  [Fact]
  public void The_smoke_stack_vents_furnace_exhaust_so_the_main_does_not_choke() {
    // A long sealed exhaust main fed by a furnace each tick, with a commissioned stack venting it.
    var (world, net) = PipeTestWorld.Run(6, capEnds: true);
    var stack = Stack(world, new BlockPos(0, 0, 0));

    for (int i = 0; i < 12; i++) {
      Furnace(world, net, FurnacePerTick);
      ReflectionHelpers.Invoke(stack, "OnProductionTick", 1f);
    }

    // The stack swallows more than the furnace spills, so the main stays well below a choking
    // over-pressure (a couple intakes' worth of slack at most).
    float vented = (float)
      ReflectionHelpers.GetField(stack, "_lastConsumedAmount")!;
    Assert.True(vented > 0f, "the stack should be venting exhaust");
    Assert.True(
      (net.State?.Volume ?? 0f) <= 2f * SmexValues.SmokestackGasIntakeVolume,
      $"a vented main should stay near empty, was {net.State?.Volume ?? 0f} L"
    );
  }

  [Fact]
  public void Without_a_stack_the_exhaust_main_backs_up_and_chokes() {
    var (world, net) = PipeTestWorld.Run(6, capEnds: true);

    for (int i = 0; i < 12; i++)
      Furnace(world, net, FurnacePerTick); // furnace spills, but nothing vents

    // No sink: the exhaust accumulates well past the main's 1-atm capacity (the backed-up,
    // furnace-choking condition the stack exists to prevent).
    float maxVolume = net.Nodes.Count * PpexValues.LitresPerPipe;
    Assert.True(
      net.State!.Volume > maxVolume,
      $"the unvented main should back up over-pressure, was {net.State!.Volume} of {maxVolume} L"
    );
  }

  #endregion

  #region Cowper stove regenerator cycle

  [Fact]
  public void A_charged_cowper_stove_blows_cool_air_back_out_as_hot_blast() {
    var rig = new CowperRig();

    // Charge: hot (1200 C) furnace exhaust soaks heat into the brick core. The transfer is gradual,
    // so run a long charging session - the regenerator builds heat over time.
    for (int i = 0; i < 200; i++)
      rig.ChargeFromExhaust(exhaustTemp: 1200f);
    float charged = rig.CoreTemperature;
    Assert.True(
      charged > 100f,
      $"the core should charge from exhaust, was {charged} C"
    );

    // Discharge: route cool (20 C) air through the charged stove - it leaves scorching hot, and the
    // core gives up its heat.
    rig.DischargeAir(airTemp: 20f);

    Assert.Equal("Air", rig.HotBlastMedium);
    Assert.True(
      rig.HotBlastVolume > 0f,
      "hot blast should be produced at the outlet"
    );
    Assert.True(
      rig.HotBlastTemperature > 100f,
      $"the air should leave far hotter than it entered, was {rig.HotBlastTemperature} C"
    );
    Assert.True(
      rig.CoreTemperature < charged,
      "discharging should cool the core"
    );
  }

  [Fact]
  public void A_cold_cowper_stove_cannot_make_hot_blast() {
    var rig = new CowperRig();

    // Never charged: discharging cool air through a cold core warms nothing.
    rig.DischargeAir(airTemp: 20f);

    Assert.True(
      rig.HotBlastTemperature <= 20f,
      $"a cold stove should not heat the blast, was {rig.HotBlastTemperature} C"
    );
  }

  // Regression (player-reported): the real two-phase cycle is discharge → CLOSE the air valve →
  // recharge from exhaust. Discharging leaves blast air sitting in the passthrough, and closing the
  // valve strands it there (a pressurised run holds well over a pipe's worth). The stove used to read
  // that stranded air as "mixing" and latch shut, refusing to ever recharge. Every other cowper test
  // charges from a pristine, empty passthrough, so none of them crossed this discharge→recharge path.
  [Fact]
  public void Recharging_after_a_discharge_is_not_blocked_by_air_left_in_the_passthrough() {
    var rig = new CowperRig();

    // Push blast air through once - this leaves leftover air stranded in the passthrough.
    rig.DischargeAir(airTemp: 20f, litres: 90f);
    Assert.True(
      rig.CoreTemperature <= 25f,
      "precondition: the core is still cold after a cold discharge"
    );

    // Air valve now closed (no fresh air); recharge from exhaust. The stranded air must not block it.
    for (int i = 0; i < 200; i++)
      rig.ChargeFromExhaust(exhaustTemp: 1200f);

    Assert.True(
      rig.CoreTemperature > 100f,
      $"a recharge must clear the stranded air and heat the core, was {rig.CoreTemperature} C"
    );
  }

  // The mix guard must still fire while air is GENUINELY flowing (both valves open): the stove can't
  // soak exhaust into the core while air streams through the passthrough, so it must not charge.
  [Fact]
  public void Exhaust_with_air_actively_flowing_is_still_treated_as_mixing() {
    var rig = new CowperRig();
    float before = rig.CoreTemperature;

    // Re-supply air every tick (open air valve) alongside the exhaust.
    for (int i = 0; i < 50; i++)
      rig.MixAirAndExhaust(exhaustTemp: 1200f);

    Assert.True(
      rig.CoreTemperature - before < 5f,
      $"the core must not charge while air is actively mixing, rose to {rig.CoreTemperature} C"
    );
  }

  #endregion
}

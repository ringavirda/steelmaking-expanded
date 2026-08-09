using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Whole-process casting scenarios (handbook casting article): molten iron poured into a canal run
/// flows down the network and is cast at the far end - into a tool mold on a pedestal, or into a
/// parked barrel on a canal tap. These exercise the molten network + the draining fittings together,
/// asserting the metal actually travels the run and ends up in the casting.
/// </summary>
public class CastingScenarioTests {
  #region Mold pedestal casting

  [Fact]
  public void Molten_iron_flows_down_the_canal_and_casts_into_a_mold_pedestal() {
    var scene = new Scene().Network("molten", s => new MoltenNetwork(s));
    var line = new CastingLine(
      scene,
      new BlockPos(0, 0, 0),
      length: 3,
      endsInPedestal: true
    );
    scene.Build();
    line.SetMold();

    line.PourIn(40); // furnace tap charges the head cell
    line.Run(20); // the network carries it to the pedestal, which casts it

    Assert.True(
      line.Pedestal!.MoldCurrentUnits > 0,
      "the mold should have taken a cast"
    );
    // Conservation: every unit is either still in the run or cast into the mold (nothing vanished).
    Assert.Equal(40, line.TotalInRun + line.Pedestal.MoldCurrentUnits);
  }

  #endregion

  #region Barrel tapping

  [Fact]
  public void A_canal_tap_drains_the_run_into_a_parked_barrel() {
    var scene = new Scene().Network("molten", s => new MoltenNetwork(s));
    var line = new CastingLine(
      scene,
      new BlockPos(0, 0, 0),
      length: 3,
      endsInPedestal: false
    );
    scene.Build();
    line.ParkBarrel(drainSpeed: 8f);

    line.PourIn(40);
    line.Run(20);

    Assert.True(
      line.Tap!.BarrelCurrentUnits > 0,
      "the barrel should have filled from the run"
    );
    Assert.Equal(40, line.TotalInRun + line.Tap.BarrelCurrentUnits);
  }

  [Fact]
  public void A_closed_tap_keeps_the_metal_in_the_run() {
    var scene = new Scene().Network("molten", s => new MoltenNetwork(s));
    var line = new CastingLine(
      scene,
      new BlockPos(0, 0, 0),
      length: 3,
      endsInPedestal: false
    );
    scene.Build();
    line.ParkBarrel(drainSpeed: 8f);
    line.Tap!.TryTogglePouring(); // close it again

    line.PourIn(40);
    line.Run(10);

    Assert.Equal(0, line.Tap.BarrelCurrentUnits); // nothing poured through the shut tap
  }

  #endregion
}

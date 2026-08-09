using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The capstone steelmaking scenario (handbook bessemer article): molten iron from a canal is charged
/// into the Bessemer converter, blown with blast drawn off a live gas network to refine it into steel,
/// and poured back out into the output canal. Exercises the molten input/output cells, the blast gas
/// network and the converter's refining process together - the final stage of an already-running line.
/// </summary>
public class BessemerScenarioTests {
  #region Full charge → refine → pour cycle

  [Fact]
  public void Molten_iron_is_charged_blown_into_steel_and_poured_to_the_output_canal() {
    var rig = new ConverterRig();

    // 1. Fill: the furnace tap pours molten iron into the input canal, the converter draws it in.
    rig.PourIronToInput(50);
    rig.Fill();
    Assert.Equal(50, rig.ContentUnits);
    Assert.True(
      rig.Input.IsCellEmpty,
      "the input cell should have drained into the vessel"
    );
    Assert.Equal("game:ingot-iron", rig.ContentCode);

    // 2. Refine: with blast flowing the converter boils the carbon out, advancing the process clock
    //    and drawing real blast off the gas network.
    rig.ChargeBlast(3f);
    float blastBefore = rig.BlastVolume;
    rig.Refine();
    Assert.True(rig.ProcessSeconds > 0f, "refining should advance while blown");
    Assert.True(
      rig.BlastVolume < blastBefore,
      "refining should consume blast from the network"
    );

    // 3. Finish the ~5-minute blow (fast-forwarded) - the charge becomes steel.
    rig.FastForwardToAlmostDone();
    rig.ChargeBlast(3f);
    rig.Refine();
    Assert.Equal("game:ingot-steel", rig.ContentCode);

    // 4. Pour: the finished steel drains into the output canal, ready to travel the molten network.
    rig.Pour();
    Assert.True(
      rig.Output.CellAmount > 0,
      "the output canal should receive the steel"
    );
    Assert.Equal("game:ingot-steel", rig.Output.CellMetalType);
  }

  // Re-use regression (the cowper lesson generalized): a converter is RE-USED for many heats. Every
  // other test runs a single charge→refine→pour from a fresh rig, so none crosses the second-heat
  // path - where leftover steel from the first heat could latch the type-mismatch guard and refuse a
  // fresh iron charge. A finished, poured converter must accept and refine a brand-new iron charge.
  [Fact]
  public void A_second_iron_heat_can_be_charged_and_refined_after_pouring_the_first() {
    var rig = new ConverterRig();

    // First heat: iron → steel → poured out, emptying the vessel.
    rig.PourIronToInput(50);
    rig.Fill();
    rig.FastForwardToAlmostDone();
    rig.ChargeBlast(3f);
    rig.Refine();
    Assert.Equal("game:ingot-steel", rig.ContentCode);
    rig.Pour();
    Assert.Equal(0, rig.ContentUnits); // vessel emptied - no leftover steel

    // Second heat: a fresh iron charge must fill (the emptied vessel claims iron cleanly, no stale
    // steel type-mismatch) and refine to steel again.
    rig.PourIronToInput(50);
    rig.Fill();
    Assert.Equal(50, rig.ContentUnits);
    Assert.Equal("game:ingot-iron", rig.ContentCode);

    rig.FastForwardToAlmostDone();
    rig.ChargeBlast(3f);
    rig.Refine();
    Assert.Equal("game:ingot-steel", rig.ContentCode);
  }

  #endregion

  #region Blast dependency

  [Fact]
  public void Without_blast_the_charge_does_not_refine() {
    var rig = new ConverterRig();
    rig.PourIronToInput(50);
    rig.Fill();

    float before = rig.ProcessSeconds;
    rig.Refine(); // gas network is empty - no blast to draw

    Assert.Equal(before, rig.ProcessSeconds, 3); // the process clock did not advance
    Assert.Equal("game:ingot-iron", rig.ContentCode); // still raw iron, not steel
  }

  #endregion

  #region Mechanical-power gate (engine→generator→transmission→converter)

  [Fact]
  public void A_turning_transmission_gives_the_converter_power() {
    var rig = new ConverterRig();
    rig.SetMechPower(speed: 1f); // the engine's MP generator spins the transmission axle

    Assert.True(
      rig.HasPower,
      "a turning transmission should power the converter"
    );
  }

  [Fact]
  public void A_stalled_transmission_leaves_the_converter_unpowered() {
    var rig = new ConverterRig();
    rig.SetMechPower(speed: 0f); // axle present but not turning (engine off / overstressed)

    Assert.False(rig.HasPower);
  }

  [Fact]
  public void With_no_mechanical_network_the_converter_has_no_power() {
    var rig = new ConverterRig();
    // The transmission block is placed but was never spun up (no MP network behind it).
    Assert.False(rig.HasPower);
  }

  #endregion
}

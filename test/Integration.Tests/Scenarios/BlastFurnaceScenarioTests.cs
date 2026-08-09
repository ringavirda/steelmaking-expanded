using ExpandedLib.Testing;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The blast furnace's primary process driven end to end (handbook blast-furnace + hot-blast): a
/// charged, lit hearth climbs past iron's melting point on whatever blast it is given, renders blast
/// mix into molten iron at a rate set by its heat margin and its air supply together, and taps it
/// into a canal. Exercises the real tick with its real peripherals via <see cref="BlastFurnaceRig"/>.
/// </summary>
public class BlastFurnaceScenarioTests {
  #region Heat ceiling

  // The ceiling is a curve over the blast temperature, not a threshold: cold air is worth a little
  // over the natural ceiling, a part-charged regenerator is worth a proportional share, and only a
  // fully charged one buys the whole boost. What the player sees is that a stove always helps.

  [Fact]
  public void Cold_blast_alone_clears_irons_melting_point() {
    // The central ruling of this model: a furnace blown with plain air melts iron on its own. Hot
    // blast is an improvement, never a requirement.
    var rig = new BlastFurnaceRig()
      .FeedBlast(20f)
      .SetState(BlastFurnaceState.Firing)
      .SetTemp(1400f);

    rig.Tick(120);

    Assert.True(
      rig.TargetTemp > SmexValues.BfIronMeltingPoint,
      $"the cold-blast ceiling {rig.TargetTemp} must clear the {SmexValues.BfIronMeltingPoint} C melting point"
    );
    Assert.Equal(BlastFurnaceState.Melting, rig.State);
  }

  [Theory]
  [InlineData(20f)] // cold blast - straight off a blower
  [InlineData(620f)] // a regenerator half way through its charge
  [InlineData(1240f)] // a fully charged regenerator (BfBlastTempReference)
  public void The_hearth_ceiling_rises_with_the_blast_temperature(
    float blastTemp
  ) {
    var rig = new BlastFurnaceRig()
      .FeedBlast(blastTemp)
      .SetState(BlastFurnaceState.Firing)
      .SetTemp(1400f);

    rig.Tick(1);

    float expected =
      SmexValues.BfNaturalMaxTemp
      + (SmexValues.BfBoostedMaxTemp - SmexValues.BfNaturalMaxTemp)
        * (blastTemp / SmexValues.BfBlastTempReference);
    Assert.Equal(expected, rig.TargetTemp, 1);
  }

  [Fact]
  public void A_full_regenerator_reaches_the_boosted_ceiling() {
    var rig = new BlastFurnaceRig()
      .FeedBlast(SmexValues.BfBlastTempReference)
      .SetState(BlastFurnaceState.Firing)
      .SetTemp(1400f);

    rig.Tick(1);

    Assert.Equal(SmexValues.BfBoostedMaxTemp, rig.TargetTemp, 1);
  }

  #endregion

  #region Melt rate

  // Melting is a rate, not a gate. Two supplies meter it - the heat margin over the melting point,
  // and the share of the air that margin asked for that actually arrived - and the readout reports
  // the product so a slow furnace can be diagnosed.

  [Fact]
  public void The_melt_rate_rises_with_the_heat_margin() {
    float Rate(float temp) {
      var rig = new BlastFurnaceRig()
        .FeedBlast(1240f)
        .SetState(BlastFurnaceState.Melting)
        .SetTemp(temp);
      rig.Tick(1);
      return rig.MeltSpeed;
    }

    float cold = Rate(SmexValues.BfNaturalMaxTemp); // cold-blast ceiling
    float hot = Rate(SmexValues.BfBoostedMaxTemp); // fully boosted

    Assert.True(
      hot > cold,
      $"a hotter hearth must melt faster; {cold}x at the natural ceiling vs {hot}x at the boosted one"
    );
    Assert.Equal(SmexValues.BfMeltSpeedMax, hot, 2);
  }

  [Fact]
  public void The_melt_rate_is_clamped_between_its_configured_bounds() {
    var barelyOver = new BlastFurnaceRig()
      .FeedBlast()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfIronMeltingPoint + 1f);
    barelyOver.Tick(1);

    // Left unclamped the formula would give BfMeltSpeedBase (0.2) just over the melting point.
    Assert.Equal(SmexValues.BfMeltSpeedMin, barelyOver.MeltSpeed, 2);
  }

  [Fact]
  public void A_starved_blast_slows_the_melt_in_proportion() {
    // Blowers that deliver only a quarter of what the hearth asks for run it at a quarter speed.
    var full = new BlastFurnaceRig()
      .FeedBlast(1240f)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp);
    full.Tick(1);

    float demandPerTuyere = full.AirRequested / 2f;
    var starved = new BlastFurnaceRig()
      .FeedBlast(1240f, demandPerTuyere / 4f)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp);
    starved.Tick(1);

    Assert.Equal(full.AirRequested, starved.AirRequested, 1);
    Assert.True(
      starved.AirDrawn < full.AirDrawn,
      "the starved furnace should draw less air than the well-fed one"
    );
    Assert.Equal(full.MeltSpeed / 4f, starved.MeltSpeed, 2);
  }

  [Fact]
  public void No_blast_at_all_stops_the_melt_without_ending_the_campaign() {
    // Air, not temperature, is what the melt is gated on: an unblown furnace holds its heat and its
    // charge and produces nothing, then picks straight back up when the blowers come back.
    var rig = new BlastFurnaceRig()
      .CutBlast()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp);

    rig.Tick(1);
    int mixBefore = rig.MixCount;
    rig.Tick(60);

    Assert.Equal(0f, rig.MeltSpeed, 3);
    Assert.Equal(0f, rig.AirDrawn, 3);
    Assert.Equal(mixBefore, rig.MixCount);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);
  }

  #endregion

  #region Melting - tapping

  [Fact]
  public void Melting_renders_blast_mix_into_molten_iron() {
    var rig = new BlastFurnaceRig()
      .FeedBlast()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(1600f)
      .SetMeltSeconds(SmexValues.BfMeltIntervalSec - 0.1f); // a melt cycle completes this tick

    rig.Tick(1);

    Assert.True(rig.MoltenIron > 0f, "a melt cycle should produce molten iron");
  }

  [Fact]
  public void A_melting_furnace_taps_molten_iron_into_the_canal() {
    var rig = new BlastFurnaceRig()
      .FeedBlast()
      .WithIronTapAndCanal()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(1600f)
      .SetMoltenIron(100f);

    rig.Tick(1);

    Assert.True(
      rig.CanalIron > 0,
      "the open tap should pour iron into the canal start"
    );
    Assert.True(
      rig.MoltenIron < 100f,
      "the furnace should give up the tapped iron"
    );
  }

  [Fact]
  public void A_full_reservoir_stalls_the_melt_instead_of_extinguishing() {
    // A plant that out-produces its taps for a moment has done nothing wrong. The furnace waits with
    // its charge intact - it used to count this as a disruption and go out 30 s later.
    var rig = new BlastFurnaceRig()
      .FeedBlast(1240f)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp)
      .SetMoltenIron(SmexValues.BfMaxMoltenIron);

    rig.Tick(1);
    int mixBefore = rig.MixCount;
    rig.Tick(60);

    Assert.Equal(BlastFurnaceState.Melting, rig.State);
    Assert.Equal(0f, rig.ExtinguishSeconds);
    Assert.Equal(mixBefore, rig.MixCount);
  }

  #endregion

  #region Falling out of the melt

  [Fact]
  public void A_hearth_below_the_melting_point_falls_back_to_firing() {
    var rig = new BlastFurnaceRig()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfIronMeltingPoint - 200f)
      .SetBelowMeltingSeconds(SmexValues.BfMeltStallSeconds - 1f)
      .CutBlast();

    rig.Tick(1);

    Assert.Equal(BlastFurnaceState.Firing, rig.State);
  }

  [Fact]
  public void Reaching_the_melting_point_transitions_to_melting_with_no_soak() {
    // The 300 s soak is gone: the hearth melts the moment it is hot enough, and how fast it melts
    // is what the heat margin buys.
    var rig = new BlastFurnaceRig()
      .FeedBlast()
      .SetState(BlastFurnaceState.Firing)
      .SetTemp(SmexValues.BfIronMeltingPoint + 1f);

    rig.Tick(1);

    Assert.Equal(BlastFurnaceState.Melting, rig.State);
  }

  [Fact]
  public void A_lit_furnace_is_never_put_out_by_a_clock() {
    // The 20-minute fuel burn used to end every campaign and take the charge with it. A furnace with
    // air and mix now runs until the player stops it.
    // Charged well past what 1500 s of melting consumes, so running out of mix cannot be what ends
    // the run - only a clock could, and there is no longer one.
    var rig = new BlastFurnaceRig(blastMix: 20000)
      .FeedBlast(1240f)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp)
      .WithIronTapAndCanal();

    rig.Tick(1500); // well past the old 1200 s BfMaxFuelBurnTime

    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);
    Assert.True(
      rig.MixCount > SmexValues.BlastMixRequiredToRun,
      "the hearth should still be charged - otherwise starvation, not a clock, ended the run"
    );
  }

  #endregion

  #region Blocked flue

  // Regression (player-reported, two independent reports): shutting the hot exhaust off to the
  // cowper stoves - which the handbook's own regenerator swap requires - counted as a disruption
  // and extinguished the furnace 30 s later, destroying a 20-minute campaign. A blocked flue now
  // stalls production instead, and picks up again when the exhaust is reopened.

  private static BlastFurnaceRig ChokedRig() =>
    new BlastFurnaceRig()
      .WithBlockedExhaust()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfIronMeltingPoint + 20f)
      .FeedBlast();

  [Fact]
  public void A_blocked_flue_does_not_count_toward_extinguishing() {
    var rig = ChokedRig();

    rig.Tick(60); // twice the disruption grace period

    Assert.True(
      rig.Furnace.IsChoked,
      "the sealed stub should have saturated - without that this asserts nothing"
    );
    Assert.Equal(0f, rig.ExtinguishSeconds);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);
  }

  [Fact]
  public void A_blocked_flue_halts_the_melt() {
    var rig = ChokedRig();
    rig.Tick(1); // one tick to populate the cached hearth count
    int mixBefore = rig.MixCount;

    rig.Tick(60);

    // No melt cycle completes while the flue is blocked, so the charge is untouched.
    Assert.True(rig.Furnace.IsChoked);
    Assert.Equal(mixBefore, rig.MixCount);
  }

  [Fact]
  public void A_blocked_flue_costs_the_furnace_its_hot_blast_boost() {
    // With no draught the regenerator loop carries nothing back, so the hearth falls to the ceiling
    // plain air would have given it - which still melts, just slowly.
    var rig = ChokedRig().FeedBlast(SmexValues.BfBlastTempReference);

    rig.Tick(10); // the sealed stub needs a few ticks to saturate

    Assert.True(rig.Furnace.IsChoked);
    Assert.Equal(SmexValues.BfNaturalMaxTemp, rig.TargetTemp, 1);
  }

  // Control: the same furnace with somewhere for its exhaust to go does consume its charge, so the
  // two assertions above are about the blockage and not about the rig never working in the first
  // place.
  [Fact]
  public void An_open_flue_keeps_the_furnace_consuming_its_charge() {
    var rig = new BlastFurnaceRig()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfIronMeltingPoint + 20f)
      .FeedBlast();
    rig.Tick(1); // one tick to populate the cached hearth count
    int mixBefore = rig.MixCount;

    rig.Tick(60);

    Assert.False(rig.Furnace.IsChoked);
    Assert.True(
      rig.MixCount < mixBefore || mixBefore == 0,
      $"an unblocked furnace should burn its charge; mix went {mixBefore} -> {rig.MixCount}"
    );
  }

  #endregion

  #region Live config

  // Regression (player-reported): /exmod config smex ... used to take effect only after a relog,
  // because the furnace cached its tunables once at load. The production tick now re-reads them, so
  // an admin change applies on the next tick without a reload.
  [Fact]
  public void A_live_config_change_to_the_melt_bounds_applies_without_a_reload() {
    float original = SmexValues.BfMeltSpeedMax;
    try {
      var rig = new BlastFurnaceRig()
        .FeedBlast(1240f)
        .SetState(BlastFurnaceState.Melting)
        .SetTemp(SmexValues.BfBoostedMaxTemp);
      rig.Tick(1);
      Assert.Equal(original, rig.MeltSpeed, 2);

      // Admin halves the maximum melt rate mid-session.
      SmexValues.Edit(c => c.BfMeltSpeedMax = original / 2f);
      rig.Tick(1);

      Assert.Equal(original / 2f, rig.MeltSpeed, 2);
    } finally {
      SmexValues.Edit(c => c.BfMeltSpeedMax = original);
    }
  }

  #endregion
}

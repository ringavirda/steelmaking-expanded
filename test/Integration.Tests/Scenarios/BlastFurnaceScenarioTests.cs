using ExpandedLib.Testing;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The blast furnace's primary process driven end to end (handbook blast-furnace + hot-blast): a
/// charged, lit hearth climbs past iron's melting point on whatever blast it is given, renders blast
/// burden into molten iron at a rate set by its heat margin and its air supply together, and taps it
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

  #endregion

  #region Lighting

  // A hearth charged below the fire threshold never reaches Firing, so it stays in the state a
  // player is in while torching the piles one by one.
  private const int PartCharge = 100;

  /// <summary>A blast furnace breathes through two tuyeres, and the rig stands up both.</summary>
  private const int FurnaceTuyeres = 2;

  [Fact]
  public void A_hearth_being_lit_breathes_before_the_furnace_is_formally_lit() {
    var rig = new BlastFurnaceRig(burden: PartCharge).FeedBlast();

    rig.Tick(1);

    Assert.Equal(BlastFurnaceState.Idle, rig.State);
    Assert.True(
      rig.AirDrawn > 0f,
      "a hearth with piles alight should draw blast before the whole charge has caught"
    );
  }

  [Fact]
  public void An_unblown_hearth_being_lit_goes_out() {
    // The reported gap: with no blower running, a player could light the charge at leisure. Piles
    // catching one by one now need the blast from the first one.
    var rig = new BlastFurnaceRig(burden: PartCharge).CutBlast();

    Assert.True(rig.HearthAlight, "the rig starts with its piles alight");

    rig.Tick((int)SmexValues.BfUnblownIgnitionSeconds + 2);

    Assert.False(
      rig.HearthAlight,
      "an unblown hearth being lit should go out inside the ignition grace"
    );
    Assert.Equal(BlastFurnaceState.Idle, rig.State);
  }

  [Fact]
  public void The_air_requirement_climbs_as_the_hearth_heats() {
    // The reported readout problem. The demand was read straight off the melt-rate factor, which is
    // clamped at its floor for everything below about 1525 C - the whole firing phase - so the
    // figure sat on one number until the furnace was already melting and looked unmetered. It has
    // to visibly rise as the furnace comes up.
    var rig = new BlastFurnaceRig()
      .FeedBlast()
      .SetState(BlastFurnaceState.Firing);

    float[] asked = new float[4];
    float[] temps =
    [
      SmexValues.BfIgnitionTemperature,
      1100f,
      1300f,
      SmexValues.BfIronMeltingPoint - 1f,
    ];
    for (int i = 0; i < temps.Length; i++) {
      rig.SetTemp(temps[i]).Tick(1);
      asked[i] = rig.AirRequested;
    }

    for (int i = 1; i < asked.Length; i++)
      Assert.True(
        asked[i] > asked[i - 1],
        $"the draw at {temps[i]} C ({asked[i]} L/s) should exceed the draw at "
          + $"{temps[i - 1]} C ({asked[i - 1]} L/s)"
      );

    // And it must arrive at the melting point already asking for every tuyere's rated volume, which
    // is what a melting hearth wants - otherwise the figure jumps the moment melting starts.
    Assert.Equal(FurnaceTuyeres * SmexValues.TuyereIntakeVolume, asked[^1], 0);
  }

  [Fact]
  public void A_furnace_vents_exactly_what_it_breathes_however_many_outlets() {
    // What is blown in comes back out ONCE. The per-outlet figure was the whole draw rather than a
    // share of it, so a two-outlet furnace vented double its own blast and one smoke stack - sized
    // correctly against the furnace - could never keep up.
    var rig = new BlastFurnaceRig()
      .WithOpenExhaust()
      .FeedBlast(1240f)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfNaturalMaxTemp);

    // Two ticks: the flue is metered off the previous tick's draw, so the first settles it.
    rig.Tick(2);

    Assert.True(rig.AirDrawn > 0f, "the furnace must be drawing blast");
    Assert.Equal(
      System.Math.Max(
        SmexValues.BfExhaustBaseVolume,
        rig.AirDrawn * SmexValues.BfExhaustPerAirDrawn
      ),
      rig.ExhaustVented,
      1
    );
  }

  [Fact]
  public void One_smoke_stack_clears_what_one_furnace_vents_flat_out() {
    // The end-to-end version of the sizing rule: drive the furnace to its melt ceiling, which is the
    // hardest it ever breathes, and the stack still has to swallow the lot.
    var rig = new BlastFurnaceRig()
      .WithOpenExhaust()
      .FeedBlast(SmexValues.BfBlastTempReference)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp);

    rig.Tick(2);

    Assert.True(
      rig.ExhaustVented <= SmexValues.SmokestackGasIntakeVolume,
      $"a furnace at its melt ceiling vents {rig.ExhaustVented} L/s but one smoke stack clears "
        + $"{SmexValues.SmokestackGasIntakeVolume} L/s"
    );
  }

  [Fact]
  public void A_melting_hearth_asks_for_its_rated_blast_and_more_when_driven() {
    // The reported readout: a furnace sitting just over the melting point showed 20 L/s of 20 - half
    // what its two tuyeres are rated for - because the demand was read off the melt-rate factor,
    // whose floor is 0.5. A melting furnace wants the full 40, and more again once the heat margin
    // drives the melt past 1x.
    float Asked(float temp) {
      var rig = new BlastFurnaceRig()
        .FeedBlast(1240f)
        .SetState(BlastFurnaceState.Melting)
        .SetTemp(temp);
      rig.Tick(1);
      return rig.AirRequested;
    }

    float rated = FurnaceTuyeres * SmexValues.TuyereIntakeVolume;

    Assert.Equal(rated, Asked(SmexValues.BfIronMeltingPoint + 1f), 0);
    Assert.Equal(rated, Asked(SmexValues.BfNaturalMaxTemp), 0);
    Assert.Equal(
      rated * SmexValues.BfMeltSpeedMax,
      Asked(SmexValues.BfBoostedMaxTemp),
      0
    );
  }

  [Fact]
  public void A_hearth_being_lit_vents_exhaust() {
    // It draws blast from the moment the first pile catches, so it has to vent it too - otherwise
    // air goes in and nothing comes out, and the flue reads clear while the furnace is working.
    var rig = new BlastFurnaceRig(burden: PartCharge)
      .WithOpenExhaust()
      .FeedBlast();

    rig.Tick(1);

    Assert.Equal(BlastFurnaceState.Idle, rig.State);
    Assert.True(
      rig.ExhaustProduced > 0f,
      "a hearth being lit should be pushing exhaust into its gas outlets"
    );
  }

  [Fact]
  public void A_blown_hearth_being_lit_stays_alight() {
    var rig = new BlastFurnaceRig(burden: PartCharge).FeedBlast();

    rig.Tick((int)SmexValues.BfUnblownIgnitionSeconds * 3);

    Assert.True(
      rig.HearthAlight,
      "blast at working pressure should keep a hearth being lit alive indefinitely"
    );
  }

  #endregion

  #region Melting - air

  [Fact]
  public void Blast_lost_stops_the_melt_at_once_and_the_campaign_on_the_grace() {
    // Blast at working pressure is a requirement, not just the melt's throttle. Losing it stops
    // production on the same tick and puts the furnace out once the disruption grace runs down -
    // sized so a deliberate cowper swap passes through it, but stopped blowers do not.
    var rig = new BlastFurnaceRig()
      .CutBlast()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp);

    rig.Tick(1);
    int burdenBefore = rig.BurdenCount;

    Assert.Equal(0f, rig.MeltSpeed, 3);
    Assert.Equal(0f, rig.AirDrawn, 3);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);

    // Inside the grace the charge is untouched and the campaign is still alive.
    rig.Tick((int)SmexValues.BfDisruptionGraceSeconds - 2);
    Assert.Equal(burdenBefore, rig.BurdenCount);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);

    rig.Tick(4);
    Assert.Equal(BlastFurnaceState.Idle, rig.State);
  }

  [Fact]
  public void Blast_returning_inside_the_grace_saves_the_campaign() {
    // The other half: the grace exists so swapping the regenerator stoves over is a normal
    // operation rather than a lost heat.
    var rig = new BlastFurnaceRig()
      .CutBlast()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp);

    rig.Tick((int)SmexValues.BfDisruptionGraceSeconds - 5);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);

    rig.FeedBlast().Tick(1);
    Assert.Equal(0f, rig.ExtinguishSeconds, 3);

    rig.Tick(60);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);
    Assert.True(
      rig.MeltSpeed > 0f,
      "the melt should pick straight back up once the blast returns"
    );
  }

  #endregion

  #region Melting - tapping

  [Fact]
  public void Melting_renders_burden_into_molten_iron() {
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
    int burdenBefore = rig.BurdenCount;
    rig.Tick(60);

    Assert.Equal(BlastFurnaceState.Melting, rig.State);
    Assert.Equal(0f, rig.ExtinguishSeconds);
    Assert.Equal(burdenBefore, rig.BurdenCount);
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
    // air and burden now runs until the player stops it.
    // Charged well past what 1500 s of melting consumes, so running out of burden cannot be what ends
    // the run - only a clock could, and there is no longer one.
    var rig = new BlastFurnaceRig(burden: 20000)
      .FeedBlast(1240f)
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfBoostedMaxTemp)
      .WithIronTapAndCanal();

    rig.Tick(1500); // well past the old 1200 s BfMaxFuelBurnTime

    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);
    Assert.True(
      rig.BurdenCount > SmexValues.BurdenRequiredToRun,
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
    int burdenBefore = rig.BurdenCount;

    rig.Tick(60);

    // No melt cycle completes while the flue is blocked, so the charge is untouched.
    Assert.True(rig.Furnace.IsChoked);
    Assert.Equal(burdenBefore, rig.BurdenCount);
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
    int burdenBefore = rig.BurdenCount;

    rig.Tick(60);

    Assert.False(rig.Furnace.IsChoked);
    Assert.True(
      rig.BurdenCount < burdenBefore || burdenBefore == 0,
      $"an unblocked furnace should burn its charge; burden went {burdenBefore} -> {rig.BurdenCount}"
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

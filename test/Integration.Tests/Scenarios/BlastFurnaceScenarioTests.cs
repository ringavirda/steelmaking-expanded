using ExpandedLib.Testing;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The blast furnace's primary process driven end to end (handbook blast-furnace + hot-blast): a
/// charged, lit hearth fed hot blast climbs past iron's melting point, enters the Melting phase,
/// renders blast mix into molten iron, and taps it into a canal - and loses the melt if the blast is
/// cut. Exercises the gated firing/melting tick with its real peripherals via <see cref="BlastFurnaceRig"/>.
/// </summary>
public class BlastFurnaceScenarioTests
{
  #region Heat + phase progression

  [Fact]
  public void Hot_blast_drives_the_furnace_past_irons_melting_point()
  {
    // A lit, firing furnace at its natural ceiling (1420 C, below iron's 1482 C melt point).
    var rig = new BlastFurnaceRig()
      .FeedBlast(950f)
      .SetState(BlastFurnaceState.Firing)
      .SetTemp(SmexValues.BfNaturalMaxTemp);

    rig.Tick(30); // hot blast pushes the boosted ceiling well above the melt point

    Assert.True(
      rig.Temp > SmexValues.BfIronMeltingPoint,
      $"hot blast should drive the furnace past {SmexValues.BfIronMeltingPoint} C, was {rig.Temp}"
    );
  }

  [Fact]
  public void Sustained_heat_above_the_melt_point_transitions_to_melting()
  {
    var rig = new BlastFurnaceRig()
      .FeedBlast()
      .SetState(BlastFurnaceState.Firing)
      .SetTemp(1600f)
      .SetSecondsAboveMelting(SmexValues.BfMeltStartDelay - 1f); // about to cross the soak time

    rig.Tick(1);

    Assert.Equal(BlastFurnaceState.Melting, rig.State);
  }

  #endregion

  #region Melting → tapping

  [Fact]
  public void Melting_renders_blast_mix_into_molten_iron()
  {
    var rig = new BlastFurnaceRig()
      .FeedBlast()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(1600f)
      .SetMeltSeconds(SmexValues.BfMeltIntervalSec - 1f); // a melt cycle completes this tick

    rig.Tick(1);

    Assert.True(rig.MoltenIron > 0f, "a melt cycle should produce molten iron");
  }

  [Fact]
  public void A_melting_furnace_taps_molten_iron_into_the_canal()
  {
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

  #endregion

  #region Losing the blast

  [Fact]
  public void Cutting_the_blast_lets_the_melt_fall_back_to_firing()
  {
    // Melting just below the melt point with the blast cut: it can only reach the natural ceiling
    // (1420 C), so it cools out of Melting and reverts to Firing once it's been cold long enough.
    var rig = new BlastFurnaceRig()
      .SetState(BlastFurnaceState.Melting)
      .SetTemp(SmexValues.BfIronMeltingPoint - 2f)
      .CutBlast();
    ReflectionHelpers.SetField(rig.Furnace, "_belowMeltingSeconds", 29f);

    rig.Tick(1);

    Assert.Equal(BlastFurnaceState.Firing, rig.State);
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
  public void A_blocked_flue_does_not_count_toward_extinguishing()
  {
    var rig = ChokedRig();

    rig.Tick(60); // twice the old 30 s extinguish threshold

    Assert.True(
      rig.Furnace.IsChoked,
      "the sealed stub should have saturated - without that this asserts nothing"
    );
    Assert.Equal(0f, rig.ExtinguishSeconds);
    Assert.NotEqual(BlastFurnaceState.Idle, rig.State);
  }

  [Fact]
  public void A_blocked_flue_halts_the_melt()
  {
    var rig = ChokedRig();
    rig.Tick(1); // one tick to populate the cached hearth count
    int mixBefore = rig.MixCount;

    rig.Tick(60);

    // No melt cycle completes while the flue is blocked, so the charge is untouched.
    Assert.True(rig.Furnace.IsChoked);
    Assert.Equal(mixBefore, rig.MixCount);
  }

  // Control: the same furnace with somewhere for its exhaust to go does consume its charge, so the
  // two assertions above are about the blockage and not about the rig never working in the first
  // place.
  [Fact]
  public void An_open_flue_keeps_the_furnace_consuming_its_charge()
  {
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

  // Regression (player-reported): /exmod config smex bfmeltstartdelay 30 used to take effect only
  // after a relog, because the furnace cached its tunables once at load. The production tick now
  // re-reads them, so an admin change applies on the next tick without a reload.
  [Fact]
  public void A_live_config_change_to_the_melt_delay_applies_without_a_reload()
  {
    float original = SmexValues.BfMeltStartDelay;
    try
    {
      var rig = new BlastFurnaceRig()
        .FeedBlast()
        .SetState(BlastFurnaceState.Firing);

      // Admin shortens the soak time mid-session.
      SmexValues.Edit(c => c.BfMeltStartDelay = 30f);
      rig.Tick(1);

      Assert.Equal(
        30f,
        (float)ReflectionHelpers.GetField(rig.Furnace, "_meltStartDelay")!,
        3
      );
    }
    finally
    {
      SmexValues.Edit(c => c.BfMeltStartDelay = original);
    }
  }

  #endregion
}

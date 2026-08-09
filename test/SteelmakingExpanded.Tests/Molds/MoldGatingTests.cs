using SteelmakingExpanded;
using SteelmakingExpanded.Molds;
using Vintagestory.API.Common;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The mold-availability policy a server admin drives through <c>/exmod molds</c>: the per-type
/// config flags (plate/ingot/rod), and which registered tool-mold codes count as disabled (read by
/// the casting gate in the tool-mold patches). These mutate the shared static <see cref="SmexValues"/>
/// config, so each test restores the defaults; <see cref="SmexValues.Edit"/>'s persistence is a no-op
/// without a loaded api.
/// </summary>
public class MoldGatingTests {
  private static void ResetAll() {
    foreach (var key in MoldGating.Keys)
      MoldGating.SetEnabled(key, true);
  }

  #region Disabled-code matching

  [Theory]
  [InlineData("smex:toolmold-blue-fired-plate", "plate")]
  [InlineData("smex:toolmold-red-raw-plate", "plate")]
  [InlineData("smex:toolmold-cream-fired-doubleingot", "ingot")]
  [InlineData("smex:toolmold-gray-fired-quadrod", "rod")]
  public void A_tool_molds_disabled_state_tracks_its_config_flag(
    string code,
    string key
  ) {
    try {
      var loc = new AssetLocation(code);

      MoldGating.SetEnabled(key, false);
      Assert.True(MoldGating.IsToolMoldDisabled(loc));

      MoldGating.SetEnabled(key, true);
      Assert.False(MoldGating.IsToolMoldDisabled(loc));
    } finally {
      ResetAll();
    }
  }

  [Theory]
  [InlineData("game:toolmold-blue-fired-plate")] // vanilla mold, not ours
  [InlineData("smex:pipe-ns")] // not a mold at all
  [InlineData("smex:toolmold-blue-fired-anvil")] // large mold, not a gated type
  public void Non_gated_codes_are_never_reported_disabled(string code) {
    try {
      foreach (var key in MoldGating.Keys)
        MoldGating.SetEnabled(key, false); // even with all three off

      Assert.False(MoldGating.IsToolMoldDisabled(new AssetLocation(code)));
    } finally {
      ResetAll();
    }
  }

  [Fact]
  public void A_null_code_is_not_disabled() {
    Assert.False(MoldGating.IsToolMoldDisabled(null));
  }

  #endregion

  #region Per-type toggling

  [Fact]
  public void Toggling_one_type_leaves_the_others_alone() {
    try {
      MoldGating.SetEnabled("plate", false);

      Assert.False(MoldGating.IsEnabled("plate"));
      Assert.True(MoldGating.IsEnabled("ingot"));
      Assert.True(MoldGating.IsEnabled("rod"));

      Assert.False(SmexValues.EnablePlateMold); // flows through to the config
    } finally {
      ResetAll();
    }
  }

  [Fact]
  public void Molds_are_enabled_by_default() {
    Assert.True(MoldGating.IsEnabled("plate"));
    Assert.True(MoldGating.IsEnabled("ingot"));
    Assert.True(MoldGating.IsEnabled("rod"));
  }

  #endregion
}

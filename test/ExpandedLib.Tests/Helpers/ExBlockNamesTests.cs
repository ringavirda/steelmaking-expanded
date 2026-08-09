using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// Block display-name decoration: the material-ish variant is folded into the name so same-shaped
/// blocks of different materials are distinguishable. The test translation service echoes keys, so
/// the assertions look for the qualifier key inside the composed name rather than English prose.
/// </summary>
public class ExBlockNamesTests {
  private const string TierKey = "exlib:refractory-tier2";

  private static Block Tiered() =>
    TestBlocks.Configure(
      new Block(),
      "smex:blastfurnace-tuyere-tier2-n",
      1,
      ("refractory", "tier2")
    );

  [Fact]
  public void The_refractory_tier_is_folded_into_an_existing_bracket_group() {
    string name = ExBlockNames.Decorate(Tiered(), "Tuyere (Straight)");

    Assert.StartsWith("Tuyere (Straight", name);
    Assert.EndsWith(")", name);
    Assert.Contains(TierKey, name);
  }

  [Fact]
  public void Decorating_twice_does_not_repeat_the_qualifier() {
    // A block whose base class already decorates, overridden to decorate again, produced
    // "Tuyere (Refractory Tier 2, Refractory Tier 2)". Which bases decorate is not visible at the
    // override site, so the guard lives here.
    Block block = Tiered();
    string once = ExBlockNames.Decorate(block, "Tuyere (Straight)");
    string twice = ExBlockNames.Decorate(block, once);

    Assert.Equal(once, twice);
  }

  [Fact]
  public void A_block_with_no_material_variant_is_left_alone() {
    Block plain = TestBlocks.Configure(new Block(), "smex:slag", 2, ("x", "y"));

    Assert.Equal("Slag", ExBlockNames.Decorate(plain, "Slag"));
  }
}

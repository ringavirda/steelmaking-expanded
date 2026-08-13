using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The tap hands back one canonical facing when picked or broken, so its four orientations stack
/// together. It has to derive that from its own code: the pre-tier <c>blastfurnacetap-{side}</c> code
/// is still known to every world that placed one before the refractory tiers arrived, where the engine
/// keeps it as a missing-block placeholder. A lookup of that code returns the placeholder rather than
/// null, so a <c>?? this</c> fallback never fires and the tap drops an unresolvable item.
/// </summary>
public class BlastFurnaceTapDropTests {
  #region Fixture

  private const string Legacy = "smex:blastfurnacetap-north";

  private static BlockBlastFurnaceTap Tap(
    TestWorld world,
    string tier,
    string side,
    int id
  ) {
    var tap = TestBlocks.Configure(
      new BlockBlastFurnaceTap(),
      $"smex:blastfurnacetap-{tier}-{side}",
      id,
      ("refractory", tier),
      ("side", side)
    );
    world.Register(tap);
    return tap;
  }

  /// <summary>A world that still remembers the pre-tier code, as an upgraded save does.</summary>
  private static TestWorld UpgradedWorld() {
    var world = new TestWorld();
    world.Register(TestBlocks.Configure(new Block(), Legacy, 90));
    return world;
  }

  #endregion

  #region The premise

  [Fact]
  public void The_legacy_code_still_resolves_in_an_upgraded_world() {
    // If this ever returned null the bug would be unreproducible here and the tests below would
    // pass for the wrong reason, so state the hazard as an assertion.
    var world = UpgradedWorld();
    Assert.NotNull(world.World.GetBlock(new AssetLocation(Legacy)));
  }

  #endregion

  #region Drops

  [Theory]
  [InlineData("tier1")]
  [InlineData("tier2")]
  [InlineData("tier3")]
  public void Breaking_a_tap_drops_its_own_tier_facing_north(string tier) {
    var world = UpgradedWorld();
    Tap(world, tier, "north", 91);
    var south = Tap(world, tier, "south", 92);

    ItemStack[] drops = south.GetDrops(world.World, new BlockPos(1, 2, 3), null);

    ItemStack drop = Assert.Single(drops);
    Assert.Equal(
      $"smex:blastfurnacetap-{tier}-north",
      drop.Collectible.Code.ToString()
    );
  }

  [Fact]
  public void Picking_a_tap_yields_its_own_tier_facing_north() {
    var world = UpgradedWorld();
    Tap(world, "tier2", "north", 91);
    var east = Tap(world, "tier2", "east", 93);

    ItemStack picked = east.OnPickBlock(world.World, new BlockPos(1, 2, 3));

    Assert.Equal(
      "smex:blastfurnacetap-tier2-north",
      picked.Collectible.Code.ToString()
    );
  }

  #endregion
}

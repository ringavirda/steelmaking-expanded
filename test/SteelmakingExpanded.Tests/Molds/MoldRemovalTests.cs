using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Testing;
using NSubstitute;
using SteelmakingExpanded.BlockMigrations;
using SteelmakingExpanded.Molds;
using Vintagestory.API.Common;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The purge list a disabled mold feeds into the shared block-migration sweep: <see cref="MoldRemoval"/>
/// reports the codes of every registered tool-mold variant whose type is currently disabled, and
/// nothing while they are enabled. (The sweep that deletes those from the world/inventories is the
/// server-coupled <c>BlockMigrationModSystem</c>, covered by the load smoke.) Restores the shared
/// static <see cref="SmexValues"/> config in a finally.
/// </summary>
public class MoldRemovalTests {
  private static Block Block(string code, int id) =>
    TestBlocks.Configure(new Block(), code, id);

  private static TestWorld WorldWith(params Block[] blocks) {
    var world = new TestWorld();
    world.World.Blocks.Returns(new List<Block>(blocks));
    return world;
  }

  [Fact]
  public void Only_disabled_mold_variants_are_listed_for_removal() {
    var plateFired = Block("smex:toolmold-blue-fired-plate", 60);
    var plateRaw = Block("smex:toolmold-red-raw-plate", 61);
    var rodFired = Block("smex:toolmold-blue-fired-quadrod", 62);
    var pedestal = Block("smex:moltencanalmoldpedestal-ns", 63); // not a mold
    var world = WorldWith(plateFired, plateRaw, rodFired, pedestal);

    try {
      MoldGating.SetEnabled("plate", false); // only plate disabled

      var codes = new MoldRemoval().GetRemovals(world.Api).ToList();

      Assert.Contains(plateFired.Code, codes); // both plate variants...
      Assert.Contains(plateRaw.Code, codes);
      Assert.DoesNotContain(rodFired.Code, codes); // rod still enabled
      Assert.DoesNotContain(pedestal.Code, codes); // never a target
    } finally {
      MoldGating.SetEnabled("plate", true);
    }
  }

  [Fact]
  public void Nothing_is_listed_while_every_mold_is_enabled() {
    var world = WorldWith(Block("smex:toolmold-blue-fired-plate", 60));
    Assert.Empty(new MoldRemoval().GetRemovals(world.Api));
  }
}

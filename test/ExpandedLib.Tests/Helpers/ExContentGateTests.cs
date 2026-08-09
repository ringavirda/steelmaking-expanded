using System.Collections.Generic;
using ExpandedLib.Helpers;
using ExpandedLib.Testing;
using NSubstitute;
using Vintagestory.API.Common;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The generic content-gate mechanism a mod uses to disable registered content: hiding matching
/// blocks/items from the creative inventory and handbook. (The recipe-removal helpers are thin
/// RemoveAll wrappers over the game's recipe registries, which the headless harness can't stand up.)
/// </summary>
public class ExContentGateTests {
  private static Block Tabbed(string code) =>
    new() {
      Code = new AssetLocation(code),
      CreativeInventoryTabs = ["general"],
    };

  [Fact]
  public void Hiding_clears_creative_tabs_and_stacks_for_matches_only() {
    var world = new TestWorld();
    var target = Tabbed("smex:toolmold-blue-fired-plate");
    var other = Tabbed("smex:toolmold-blue-fired-quadrod");
    world.World.Blocks.Returns(new List<Block> { target, other });
    world.World.Items.Returns(new List<Item>());

    int hidden = ExContentGate.HideFromCreativeAndHandbook(
      world.Api,
      obj => obj.Code.Path.EndsWith("-plate")
    );

    Assert.Equal(1, hidden);
    Assert.Empty(target.CreativeInventoryTabs); // hidden -> also drops from the handbook
    Assert.Null(target.CreativeInventoryStacks);
    Assert.NotEmpty(other.CreativeInventoryTabs); // untouched
  }

  /// <summary>
  /// The tab array must survive as an empty array, never a null. Game code walks every collectible
  /// and reads <c>CreativeInventoryTabs.Length</c> with no null guard - AttachableInteractionHelp
  /// does it while building the interaction help for any attachable entity - so a null here crashes
  /// the client the moment a player looks at a boat or a mount. Hiding is expressed by length, so
  /// an empty array is both safe and equivalent.
  /// </summary>
  [Fact]
  public void Hiding_leaves_an_empty_tab_array_rather_than_a_null() {
    var world = new TestWorld();
    var target = Tabbed("smex:toolmold-blue-fired-plate");
    world.World.Blocks.Returns(new List<Block> { target });
    world.World.Items.Returns(new List<Item>());

    ExContentGate.HideFromCreativeAndHandbook(world.Api, _ => true);

    Assert.NotNull(target.CreativeInventoryTabs);
    Assert.Equal(0, target.CreativeInventoryTabs.Length);
  }

  [Fact]
  public void Hiding_returns_zero_when_nothing_matches() {
    var world = new TestWorld();
    var block = Tabbed("smex:toolmold-blue-fired-quadrod");
    world.World.Blocks.Returns(new List<Block> { block });
    world.World.Items.Returns(new List<Item>());

    int hidden = ExContentGate.HideFromCreativeAndHandbook(
      world.Api,
      obj => obj.Code.Path.EndsWith("-plate")
    );

    Assert.Equal(0, hidden);
    Assert.NotNull(block.CreativeInventoryTabs);
  }
}

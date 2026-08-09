using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.GameContent;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Mold classification: large tool molds (anvil, helve hammer) are cast in the canal tap, everything
/// else fits the pedestal. Keyed off the vanilla <c>tooltype</c> variant of a <see cref="BlockToolMold"/>.
/// </summary>
public class MoldKindsTests {
  private static BlockToolMold Mold(string toolType) =>
    TestBlocks.Configure(
      new BlockToolMold(),
      $"smex:toolmold-{toolType}",
      1,
      ("tooltype", toolType)
    );

  [Theory]
  [InlineData("anvil")]
  [InlineData("helvehammer")]
  public void IsLarge_recognises_tap_only_tool_molds(string toolType) {
    var mold = Mold(toolType);
    Assert.True(MoldKinds.IsLarge(mold));
    Assert.False(MoldKinds.FitsPedestal(mold));
  }

  [Theory]
  [InlineData("ingot")]
  [InlineData("pickaxe")]
  public void Small_molds_fit_the_pedestal(string toolType) {
    var mold = Mold(toolType);
    Assert.False(MoldKinds.IsLarge(mold));
    Assert.True(MoldKinds.FitsPedestal(mold));
  }

  [Fact]
  public void Non_tool_mold_blocks_are_neither() {
    var plain = TestBlocks.Configure(new Block(), "game:rock", 2);
    Assert.False(MoldKinds.IsLarge(plain));
    Assert.False(MoldKinds.FitsPedestal(plain));
  }

  [Fact]
  public void Null_block_is_neither() {
    Assert.False(MoldKinds.IsLarge(null));
    Assert.False(MoldKinds.FitsPedestal(null));
  }
}

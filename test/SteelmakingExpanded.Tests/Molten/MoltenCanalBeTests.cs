using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The molten-canal cell block entity: each cell owns its metal (amount, type, temperature) plus the
/// sealed/solidified latches that sever the network. This covers the save/reload round trip, the
/// connectivity rules, and the derived state - the parts that do not need a resolved game item.
/// </summary>
public class MoltenCanalBeTests {
  // A non-null world is required: BlockEntity.FromTreeAttributes dereferences it to resolve blocks.
  private static readonly TestWorld ResolveWorld = new();

  private static BlockEntityMoltenCanal Canal(TestWorld? world = null) {
    var be = new BlockEntityMoltenCanal {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanal-straight-ns",
        1,
        ("type", "straight"),
        ("orientation", "ns")
      ),
    };
    (world ?? ResolveWorld).Attach(be);
    return be;
  }

  /// <summary>Loads cell state through the save tree (the only public way to set the protected fields).</summary>
  private static BlockEntityMoltenCanal CanalWith(
    int amount,
    string metal,
    float temp,
    bool solidified = false,
    bool sealed_ = false
  ) {
    var be = Canal();
    var tree = new TreeAttribute();
    tree.SetInt("cellAmount", amount);
    tree.SetString("cellMetalType", metal);
    tree.SetFloat("cellTemperature", temp);
    tree.SetBool("solidified", solidified);
    tree.SetBool("sealed", sealed_);
    be.FromTreeAttributes(tree, ResolveWorld.World);
    return be;
  }

  [Fact]
  public void Cell_state_round_trips_through_the_tree() {
    var src = Canal();
    var srcTree = new TreeAttribute();
    srcTree.SetInt("cellAmount", 42);
    srcTree.SetString("cellMetalType", "game:ingot-iron");
    srcTree.SetFloat("cellTemperature", 1300f);
    srcTree.SetBool("solidified", true);
    srcTree.SetBool("sealed", false);
    src.FromTreeAttributes(srcTree, ResolveWorld.World);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var restored = Canal();
    restored.FromTreeAttributes(tree, ResolveWorld.World);

    Assert.Equal(42, restored.CellAmount);
    Assert.Equal("game:ingot-iron", restored.CellMetalType);
    Assert.Equal(1300f, restored.CellTemperature, 3);
    Assert.True(restored.Solidified);
  }

  [Fact]
  public void An_empty_cell_is_never_solidified_on_load() {
    // Scrubs a phantom solidified+empty combination from an old save.
    var be = CanalWith(
      amount: 0,
      metal: "game:ingot-iron",
      temp: 0f,
      solidified: true
    );
    Assert.False(be.Solidified);
    Assert.Equal("", be.CellMetalType);
    Assert.True(be.IsCellEmpty);
  }

  [Fact]
  public void Sealed_or_solidified_cells_sever_the_network() {
    Assert.True(
      CanalWith(10, "game:ingot-iron", 1400f, sealed_: true)
        .IsConnectionBroken()
    );
    Assert.True(
      CanalWith(10, "game:ingot-iron", 1400f, solidified: true)
        .IsConnectionBroken()
    );
    Assert.False(CanalWith(10, "game:ingot-iron", 1400f).IsConnectionBroken());
  }

  [Fact]
  public void HasMoltenMetal_requires_liquid_metal_present() {
    Assert.True(CanalWith(10, "game:ingot-iron", 1400f).HasMoltenMetal);
    Assert.False(CanalWith(0, "", 0f).HasMoltenMetal);
    Assert.False(
      CanalWith(10, "game:ingot-iron", 400f, solidified: true).HasMoltenMetal
    );
  }

  [Fact]
  public void WouldSpillOnRemoval_only_for_unsolidified_liquid() {
    Assert.True(CanalWith(10, "game:ingot-iron", 1400f).WouldSpillOnRemoval());
    Assert.False(
      CanalWith(10, "game:ingot-iron", 400f, solidified: true)
        .WouldSpillOnRemoval()
    );
    Assert.False(CanalWith(0, "", 0f).WouldSpillOnRemoval());
  }

  [Theory]
  [InlineData(0, 400f, 0)] // empty -> no glow regardless of temp
  [InlineData(10, 400f, 0)] // below the 500 C glow floor
  [InlineData(10, 800f, 10)] // (800-500)/30
  public void GlowLightLevel_reflects_fill_and_temperature(
    int amount,
    float temp,
    int expected
  ) {
    var be = CanalWith(amount, amount > 0 ? "game:ingot-iron" : "", temp);
    Assert.Equal((byte)expected, be.GlowLightLevel);
  }

  [Fact]
  public void SetSealed_toggles_the_seal() {
    var world = new TestWorld();
    var be = Canal(world);
    Assert.False(be.Sealed);

    be.SetSealed(true);
    Assert.True(be.Sealed);
    Assert.True(be.IsConnectionBroken());

    be.SetSealed(false);
    Assert.False(be.Sealed);
  }
}

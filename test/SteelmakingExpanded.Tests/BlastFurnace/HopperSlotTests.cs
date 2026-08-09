using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using SteelmakingExpanded.Compat;
using Vintagestory.API.Common;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The reinforced hopper's typed feed slots: iron slots take crushed iron ore (and reclaimed
/// blastmix), coke slots take crushed coke, flux slots take lime - everything else is refused. This
/// is the gate that stops the wrong material reaching the bell hopper's blast-mix recipe.
/// </summary>
public class HopperSlotTests {
  private static ItemSlot Source(string code) {
    var item = new Item { Code = new AssetLocation(code) };
    return new DummySlot(new ItemStack(item));
  }

  private static ItemSlotBlastFurnace Slot(string allowedType) {
    var inv = new InventoryBlastFurnace(8, "test", null, null);
    // Index by allowed type: 0 = iron, 2 = coke, 3 = lime (see NewSlot).
    int i = allowedType switch {
      "iron" => 0,
      "coke" => 2,
      _ => 3,
    };
    return (ItemSlotBlastFurnace)inv[i];
  }

  #region Slot typing

  [Theory]
  [InlineData(0, "iron")]
  [InlineData(1, "iron")]
  [InlineData(2, "coke")]
  [InlineData(3, "lime")]
  [InlineData(4, "iron")]
  [InlineData(5, "iron")]
  [InlineData(6, "coke")]
  [InlineData(7, "lime")]
  public void NewSlot_assigns_the_feed_type_per_index(
    int index,
    string expected
  ) {
    var inv = new InventoryBlastFurnace(8, "test", null, null);
    Assert.Equal(expected, ((ItemSlotBlastFurnace)inv[index]).AllowedType);
  }

  #endregion

  #region CanTakeFrom

  [Fact]
  public void Iron_slot_takes_crushed_iron_and_reclaimed_blastmix() {
    var iron = Slot("iron");
    Assert.True(iron.CanTakeFrom(Source("game:crushed-iron")));
    Assert.True(iron.CanTakeFrom(Source("smex:blastmix")));
  }

  [Fact]
  public void Iron_slot_refuses_coke_and_lime() {
    var iron = Slot("iron");
    Assert.False(iron.CanTakeFrom(Source("game:coke")));
    Assert.False(iron.CanTakeFrom(Source("game:lime")));
  }

  [Fact]
  public void Coke_slot_takes_whole_coke_not_the_retired_crushed_intermediate() {
    var coke = Slot("coke");
    Assert.True(coke.CanTakeFrom(Source("game:coke")));
    Assert.False(coke.CanTakeFrom(Source("game:crushed-coke")));
    Assert.False(coke.CanTakeFrom(Source("game:crushed-iron")));
  }

  [Fact]
  public void Lime_slot_takes_only_lime() {
    var lime = Slot("lime");
    Assert.True(lime.CanTakeFrom(Source("game:lime")));
    Assert.False(lime.CanTakeFrom(Source("smex:blastmix")));
  }

  #endregion

  #region IronOreCompat

  [Theory]
  [InlineData("crushed-iron", true)]
  [InlineData("crushed-iron-magnetite", true)]
  [InlineData("coke", false)]
  [InlineData("lime", false)]
  public void IsCrushedIronOre_matches_the_crushed_iron_prefix(
    string path,
    bool expected
  ) {
    Assert.Equal(expected, IronOreCompat.IsCrushedIronOre(path));
  }

  #endregion
}

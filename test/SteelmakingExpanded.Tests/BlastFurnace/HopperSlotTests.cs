using System.Linq;
using NSubstitute;
using SteelmakingExpanded.BlockStructures.BlastFurnace;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using SteelmakingExpanded.Compat;
using Vintagestory.API.Common;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The reinforced hopper's typed feed slots: iron slots take crushed iron ore (and reclaimed
/// burden), coke slots take crushed coke, flux slots take lime - everything else is refused. This
/// is the gate that stops the wrong material reaching the bell hopper's burden recipe.
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
  public void Iron_slot_takes_crushed_iron_and_reclaimed_burden() {
    var iron = Slot("iron");
    Assert.True(iron.CanTakeFrom(Source("game:crushed-iron")));
    Assert.True(iron.CanTakeFrom(Source("smex:burden")));
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
    Assert.False(lime.CanTakeFrom(Source("smex:burden")));
  }

  #endregion

  #region IronOreCompat

  private static ICoreAPI ApiWithMods(params string[] enabled) {
    var api = Substitute.For<ICoreAPI>();
    var modLoader = Substitute.For<IModLoader>();
    modLoader
      .IsModEnabled(Arg.Any<string>())
      .Returns(call => enabled.Contains(call.Arg<string>()));
    api.ModLoader.Returns(modLoader);
    return api;
  }

  [Theory]
  [InlineData("crushed-iron", true)]
  [InlineData("crushed-iron-magnetite", false)]
  [InlineData("coke", false)]
  [InlineData("lime", false)]
  public void IsCrushedIronOre_matches_whole_codes_not_prefixes(
    string path,
    bool expected
  ) {
    Assert.Equal(expected, IronOreCompat.IsCrushedIronOre(path));
  }

  [Fact]
  public void Init_without_an_overhaul_takes_the_vanilla_crushed_iron() {
    try {
      IronOreCompat.Init(ApiWithMods());

      Assert.True(IronOreCompat.IsCrushedIronOre("crushed-iron"));
      Assert.True(IronOreCompat.IsIronNugget("nugget-limonite"));
      Assert.False(IronOreCompat.IsIronNugget("roasted-nugget-iron"));
    } finally {
      IronOreCompat.Init(ApiWithMods());
    }
  }

  [Fact]
  public void Init_with_industrialstory_swaps_the_vanilla_feed_for_its_own() {
    try {
      IronOreCompat.Init(ApiWithMods("industrialstory"));

      Assert.False(IronOreCompat.IsCrushedIronOre("crushed-iron"));
      Assert.True(IronOreCompat.IsCrushedIronOre("crushed-hematite"));
      Assert.True(IronOreCompat.IsRoastedIronOre("roasted-crushed-iron"));
      Assert.True(IronOreCompat.IsRoastedIronOre("roasted-nugget-iron"));
      Assert.True(IronOreCompat.IsIronFeed("roasted-nugget-iron"));
    } finally {
      IronOreCompat.Init(ApiWithMods());
    }
  }

  // What the same ore is worth down the bloomery route instead: a vanilla iron nugget smelts at 20
  // to the ingot, and an ingot is 100 units of metal.
  private const float BloomeryUnitsPerOre = 5f;

  [Fact]
  public void Roasted_ore_is_worth_more_than_the_raw_feed_it_came_from() {
    Assert.True(BurdenValue.OrePerRoasted > BurdenValue.OrePerNugget);
    Assert.True(BurdenValue.OrePerRoasted > BurdenValue.OrePerCrushed);
  }

  [Theory]
  [InlineData(12, 1.70)] // raw ore, crushed or nugget
  [InlineData(14, 1.98)] // roasted
  public void Furnace_pays_the_agreed_multiple_of_the_bloomery_route(
    int oreUnits,
    double expectedMultiple
  ) {
    // The furnace's advantage over a bloomery is the number being tuned here, so it is stated
    // against that route rather than in raw molten units. Retuning any of BfIronPerMeltCycle,
    // the Hopper*Required counts or HopperRoastedOreBonus moves these.
    Assert.Equal(
      expectedMultiple,
      BurdenValue.IronPerOreUnits(oreUnits) / BloomeryUnitsPerOre,
      1
    );
  }

  [Fact]
  public void Crushed_ore_returns_more_iron_than_the_bit_it_could_be_made_from() {
    // The premise behind refusing vanilla crushed iron under IndustrialStory: there the furnace's
    // own iron bits are the only thing that pulverises into it, and one bit costs
    // MoltenUnitsPerBit to cast but buys OrePerCrushed of burden. If a retune ever makes this
    // exchange break even, the guard in Init has lost its reason.
    Assert.True(
      BurdenValue.IronPerOreUnits(BurdenValue.OrePerCrushed)
        > SmexValues.MoltenUnitsPerBit
    );
  }

  #endregion
}

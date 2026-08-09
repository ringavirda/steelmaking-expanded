using SteelmakingExpanded.BlockNetworkMolten;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The single round-trip of a carried barrel/mold's metal through its <c>blockEntityAttributes</c>
/// tree - the same trees the vanilla tool mold and the mod's barrel persist, so a parked item
/// restores seamlessly. Molds additionally get the vanilla-compatible shattered/meshAngle keys.
/// </summary>
public class MoltenContentsTests {
  private static ITreeAttribute? BeData(ItemStack stack) =>
    stack.Attributes["blockEntityAttributes"] as ITreeAttribute;

  [Fact]
  public void Write_is_a_noop_when_there_is_no_content() {
    var stack = new ItemStack();
    MoltenContents.Write(stack, MoltenContents.BarrelUnitsKey, null, 5);
    Assert.Null(BeData(stack)); // nothing carried, tree stays clean
  }

  [Fact]
  public void Write_is_a_noop_when_units_are_non_positive() {
    var stack = new ItemStack();
    MoltenContents.Write(
      stack,
      MoltenContents.BarrelUnitsKey,
      new ItemStack(),
      0
    );
    Assert.Null(BeData(stack));
  }

  [Fact]
  public void Write_stores_content_and_units_under_the_given_key() {
    var stack = new ItemStack();
    MoltenContents.Write(
      stack,
      MoltenContents.BarrelUnitsKey,
      new ItemStack(),
      42
    );

    var tree = BeData(stack);
    Assert.NotNull(tree);
    Assert.Equal(42, tree!.GetInt(MoltenContents.BarrelUnitsKey));
    Assert.NotNull(tree.GetItemstack("contents"));
  }

  [Fact]
  public void Write_adds_vanilla_mold_compat_keys_for_the_mold_key_only() {
    var mold = new ItemStack();
    MoltenContents.Write(mold, MoltenContents.MoldUnitsKey, new ItemStack(), 3);
    var moldTree = BeData(mold)!;
    Assert.False(moldTree.GetBool("shattered"));
    Assert.Equal(0f, moldTree.GetFloat("meshAngle"));

    // The barrel key must NOT inject the mold-only keys.
    var barrel = new ItemStack();
    MoltenContents.Write(
      barrel,
      MoltenContents.BarrelUnitsKey,
      new ItemStack(),
      3
    );
    Assert.False(BeData(barrel)!.HasAttribute("meshAngle"));
  }

  [Fact]
  public void Read_returns_nothing_for_a_bare_stack() {
    var (content, units) = MoltenContents.Read(
      new ItemStack(),
      MoltenContents.MoldUnitsKey,
      null!
    );
    Assert.Null(content);
    Assert.Equal(0, units);
  }

  [Fact]
  public void Read_recovers_unit_count_without_contents() {
    var stack = new ItemStack();
    var beData = new TreeAttribute();
    beData.SetInt(MoltenContents.MoldUnitsKey, 7);
    stack.Attributes["blockEntityAttributes"] = beData;

    var (content, units) = MoltenContents.Read(
      stack,
      MoltenContents.MoldUnitsKey,
      null!
    );
    Assert.Null(content); // no "contents" stored
    Assert.Equal(7, units);
  }
}

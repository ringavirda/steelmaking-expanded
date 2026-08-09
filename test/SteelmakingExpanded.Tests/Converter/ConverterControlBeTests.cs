using ExpandedLib.Testing;
using NSubstitute;
using SteelmakingExpanded.BlockStructures.Converter;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The Bessemer converter control's persisted state machine and its break handoff. The full
/// production tick is gated on a constructed vessel plus four aligned peripherals (transmission MP
/// network, gas blast, molten in/out cells), which is out of scope to fully fake; this pins the
/// operational state that survives a reload and the charge-clearing on break.
/// </summary>
public class ConverterControlBeTests {
  private static readonly TestWorld ResolveWorld = new();

  private static BlockEntityConverterControl Control(TestWorld? world = null) {
    var be = new BlockEntityConverterControl {
      Pos = new BlockPos(0, 8, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:converterbessemercontrol-north",
        1,
        ("side", "north")
      ),
    };
    (world ?? ResolveWorld).Attach(be);
    return be;
  }

  private static ItemStack IronCharge(TestWorld world) {
    var item = new Item { Code = new AssetLocation("game:ingot-iron") };
    return new ItemStack(item, 1);
  }

  [Fact]
  public void OpState_defaults_to_normal() {
    Assert.Equal(ConverterOpState.Normal, Control().OpState);
  }

  [Fact]
  public void Operational_state_round_trips_through_the_tree() {
    var src = Control();
    ReflectionHelpers.SetProperty(src, "OpState", ConverterOpState.Filling);
    ReflectionHelpers.SetField(src, "_contentUnits", 30);
    ReflectionHelpers.SetField(src, "_processSeconds", 12f);
    ReflectionHelpers.SetField(src, "_solidified", true);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Control();
    dst.FromTreeAttributes(tree, ResolveWorld.World);

    Assert.Equal(ConverterOpState.Filling, dst.OpState);
    Assert.Equal(30, (int)ReflectionHelpers.GetField(dst, "_contentUnits")!);
    Assert.Equal(
      12f,
      (float)ReflectionHelpers.GetField(dst, "_processSeconds")!,
      3
    );
    Assert.True((bool)ReflectionHelpers.GetField(dst, "_solidified")!);
  }

  /// <summary>
  /// Every figure the look-at panel prints, checked as a set. <c>GetBlockInfo</c> runs CLIENT-side
  /// off the synced tree, and the blast figures come from a pipe network that exists only on the
  /// server - so one left out of <c>ToTreeAttributes</c> is not stale, it reads as zero whatever the
  /// server measured. The blast pressure shipped that way and showed 0 atm on a converter with 2.6
  /// atm standing at its intake.
  /// </summary>
  [Fact]
  public void Every_field_the_readout_prints_reaches_the_client() {
    // _processTemp and _convSpeed are deliberately absent: nothing prints them any more, so they
    // are recomputed each tick rather than sent.
    string[] readoutFields = ["_blastPressure", "_airDrawn", "_airDemand"];

    var src = Control();
    // A distinct value per field, so a mix-up between two fails as loudly as a missing one.
    for (int i = 0; i < readoutFields.Length; i++)
      ReflectionHelpers.SetField(src, readoutFields[i], 3f + i);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Control();
    dst.FromTreeAttributes(tree, ResolveWorld.World);

    for (int i = 0; i < readoutFields.Length; i++)
      Assert.Equal(
        3f + i,
        (float)ReflectionHelpers.GetField(dst, readoutFields[i])!,
        2
      );
  }

  [Fact]
  public void Breaking_a_solidified_converter_returns_drops_and_clears_the_charge() {
    var world = new TestWorld();
    // The solidified-bits drop resolves the bit item; any non-null item yields a stack.
    world
      .World.GetItem(Arg.Any<AssetLocation>())
      .Returns(new Item { Code = new AssetLocation("game:metalbit-iron") });

    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", IronCharge(world));
    ReflectionHelpers.SetField(be, "_contentUnits", 20);
    ReflectionHelpers.SetField(be, "_solidified", true);

    var drops = be.OnConverterBroken();

    Assert.NotNull(drops); // a solid plug scatters recoverable bits
    Assert.Equal(ConverterOpState.Normal, be.OpState);
    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
    Assert.Null(ReflectionHelpers.GetField(be, "_content"));
  }

  [Fact]
  public void Breaking_a_liquid_converter_clears_the_charge_without_drops() {
    var world = new TestWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", IronCharge(world));
    ReflectionHelpers.SetField(be, "_contentUnits", 20);
    ReflectionHelpers.SetField(be, "_solidified", false); // still molten - it spills, no solid drop

    var drops = be.OnConverterBroken();

    Assert.Null(drops);
    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
    Assert.Null(ReflectionHelpers.GetField(be, "_content"));
  }
}

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
/// The converter vessel is a thin shell that mirrors the operational state pushed by its control
/// brain and forwards the break handoff to it. Covers the mirror update, the control link + state
/// persistence, and the solidified-drop forwarding on break.
/// </summary>
public class ConverterBessemerTests {
  private static BlockEntityConverterBessemer Vessel(
    TestWorld world,
    BlockPos pos
  ) {
    var be = new BlockEntityConverterBessemer {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:converterbessemer-north",
        1,
        ("side", "north")
      ),
    };
    world.Place(pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  private static BlockEntityConverterControl Control(
    TestWorld world,
    BlockPos pos
  ) {
    var be = new BlockEntityConverterControl {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:converterbessemercontrol-north",
        2,
        ("side", "north")
      ),
    };
    world.Place(pos, be.Block, be);
    world.Attach(be);
    return be;
  }

  #region Mirror

  [Fact]
  public void A_fresh_vessel_is_unconstructed_and_not_solidified() {
    var be = Vessel(new TestWorld(), new BlockPos(0, 8, 0));
    Assert.False(be.IsConstructed);
    Assert.False(be.IsSolidified);
  }

  [Fact]
  public void UpdateMirror_reflects_the_pushed_state() {
    var be = Vessel(new TestWorld(), new BlockPos(0, 8, 0));

    be.UpdateMirror(
      solidified: true,
      chargeUnits: 200,
      ConverterOpState.Pouring
    );

    Assert.True(be.IsSolidified);
    Assert.Equal(200, (int)ReflectionHelpers.GetField(be, "_chargeUnits")!);
    Assert.Equal(
      ConverterOpState.Pouring,
      (ConverterOpState)ReflectionHelpers.GetField(be, "_opState")!
    );
  }

  #endregion

  #region Break handoff

  [Fact]
  public void CollectBreakDrops_forwards_to_the_linked_control() {
    var world = new TestWorld();
    world.RegisterItem("game:metalbit-iron");
    var ironItem = world.RegisterItem("game:ingot-iron", 1500f);

    var control = Control(world, new BlockPos(0, 7, 0));
    ReflectionHelpers.SetField(control, "_content", new ItemStack(ironItem, 1));
    ReflectionHelpers.SetField(control, "_contentUnits", 20);
    ReflectionHelpers.SetField(control, "_solidified", true);

    var vessel = Vessel(world, new BlockPos(0, 8, 0));
    vessel.LinkControl(control.Pos);

    var drops = vessel.CollectBreakDrops();

    Assert.NotNull(drops); // a solidified charge scatters recoverable bits
    Assert.Equal(0, (int)ReflectionHelpers.GetField(control, "_contentUnits")!); // control cleared
  }

  [Fact]
  public void CollectBreakDrops_is_null_with_no_control_linked() {
    var be = Vessel(new TestWorld(), new BlockPos(0, 8, 0));
    Assert.Null(be.CollectBreakDrops());
  }

  #endregion

  #region Serialization

  [Fact]
  public void Mirror_state_and_control_link_round_trip_through_the_tree() {
    var world = new TestWorld();
    var src = Vessel(world, new BlockPos(0, 8, 0));
    src.LinkControl(new BlockPos(3, 7, 4));
    src.UpdateMirror(
      solidified: true,
      chargeUnits: 150,
      ConverterOpState.Filling
    );

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var dst = Vessel(world, new BlockPos(0, 8, 0));
    dst.FromTreeAttributes(tree, world.World);

    Assert.True(dst.IsSolidified);
    Assert.Equal(150, (int)ReflectionHelpers.GetField(dst, "_chargeUnits")!);
    var ctrl = (BlockPos)ReflectionHelpers.GetField(dst, "_controlPos")!;
    Assert.Equal(new BlockPos(3, 7, 4), ctrl);
  }

  #endregion
}

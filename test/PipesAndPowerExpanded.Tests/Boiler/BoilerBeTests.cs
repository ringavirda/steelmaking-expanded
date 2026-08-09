using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockStructures.Boiler;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The boiler block entity's persistence and lid state. The operating-state fields (water, steam,
/// phase, lid, timers) must survive a save/reload round trip, and the manual lid toggle flips the
/// vent state. The pressure/temperature formulas are pinned separately in <see cref="BoilerMathTests"/>.
/// </summary>
public class BoilerBeTests {
  private static BlockEntityBoilerCornish Boiler(TestWorld? world = null) {
    var be = new BlockEntityBoilerCornish {
      Pos = new BlockPos(0, 0, 0),
      // Base BlockEntity.ToTreeAttributes reads Block.Code, so a placed block is required.
      Block = TestBlocks.Configure(
        new Vintagestory.API.Common.Block(),
        "ppex:boiler-cornish-north",
        1
      ),
    };
    world?.Attach(be);
    return be;
  }

  [Fact]
  public void Operating_state_round_trips_through_the_tree() {
    var src = Boiler();
    ReflectionHelpers.SetField(src, "_waterVolume", 300f);
    ReflectionHelpers.SetField(src, "_steamVolume", 200f);
    ReflectionHelpers.SetField(
      src,
      "_state",
      BlockEntityBoiler.BoilerState.Boiling
    );
    ReflectionHelpers.SetField(src, "_heatingSeconds", 90f);
    ReflectionHelpers.SetField(src, "_shutdownSeconds", 5f);
    // LidOpen's auto-property setter is private; set its backing field directly.
    ReflectionHelpers.SetField(src, "<LidOpen>k__BackingField", true);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var world = new TestWorld();
    var dst = Boiler(world);
    dst.FromTreeAttributes(tree, world.World);

    Assert.Equal(
      300f,
      (float)ReflectionHelpers.GetField(dst, "_waterVolume")!,
      3
    );
    Assert.Equal(
      200f,
      (float)ReflectionHelpers.GetField(dst, "_steamVolume")!,
      3
    );
    Assert.Equal(
      BlockEntityBoiler.BoilerState.Boiling,
      ReflectionHelpers.GetField(dst, "_state")
    );
    Assert.Equal(
      90f,
      (float)ReflectionHelpers.GetField(dst, "_heatingSeconds")!,
      3
    );
    Assert.True(dst.LidOpen);
    // Derived pressure reconstructs from the restored volumes: 200 / (800 - 300) = 0.4.
    Assert.Equal(0.4f, dst.InternalPressure, 3);
  }

  [Fact]
  public void Lid_defaults_closed() {
    Assert.False(Boiler().LidOpen);
  }

  [Fact]
  public void ToggleLid_flips_the_lid_state() {
    var world = new TestWorld();
    var be = Boiler(world);

    be.ToggleLid();
    Assert.True(be.LidOpen);

    be.ToggleLid();
    Assert.False(be.LidOpen);
  }

  [Fact]
  public void A_fresh_boiler_serializes_an_idle_closed_state() {
    var be = Boiler();
    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    Assert.Equal(
      (int)BlockEntityBoiler.BoilerState.Idle,
      tree.GetInt("boilerState")
    );
    Assert.False(tree.GetBool("lidOpen"));
    Assert.Equal(0f, tree.GetFloat("waterVolume"), 3);
  }
}

using System;
using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The furnace's structure rotation, which drives both the build projection and every
/// structure-local offset (hearth, tuyeres, gas outlets, taps). The door carries no orientation
/// variant - its facing lives only in <c>BEBehaviorDoor.RotateYRad</c> - so the angle is derived
/// rather than read off the block code, and that derivation is what these pin.
/// </summary>
public class BlastFurnaceRotationTests {
  private static BlockEntityBlastFurnace Furnace(
    TestWorld world,
    float baseAngleRad
  ) {
    var be = new BlockEntityBlastFurnace {
      Pos = new BlockPos(0, 16, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:blastfurnacedoor",
        1,
        ("dummy", "x")
      ),
      BaseAngleRad = baseAngleRad,
    };
    world.Attach(be);
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation");
    return be;
  }

  private static int AngleOf(BlockEntityBlastFurnace be) =>
    (int)ReflectionHelpers.GetField(be, "_currentAngle")!;

  [Theory]
  [InlineData(0f, 0)]
  [InlineData(MathF.PI / 2f, 90)]
  [InlineData(MathF.PI, 180)]
  [InlineData(3f * MathF.PI / 2f, 270)]
  public void The_structure_angle_follows_the_door_rotation(
    float baseAngleRad,
    int expectedDegrees
  ) {
    var be = Furnace(new TestWorld(), baseAngleRad);

    Assert.Equal(expectedDegrees, AngleOf(be));
  }

  [Theory]
  [InlineData(2f * MathF.PI, 0)]
  [InlineData(2f * MathF.PI + MathF.PI / 2f, 90)]
  [InlineData(3f * MathF.PI, 180)]
  public void An_angle_past_a_full_turn_resolves_to_its_quadrant(
    float baseAngleRad,
    int expectedDegrees
  ) {
    // Placement adds a half turn to the door's facing, so the derived angle routinely runs past a
    // full revolution and must wrap rather than snapping to the top quadrant.
    var be = Furnace(new TestWorld(), baseAngleRad);

    Assert.Equal(expectedDegrees, AngleOf(be));
  }

  [Fact]
  public void An_underived_angle_is_left_alone_rather_than_latched_to_zero() {
    // -1 is the "not yet derived" sentinel. Treating it as an angle used to snap the structure to
    // north and, because the result is non-negative, latch it there permanently.
    var be = Furnace(new TestWorld(), -1f);

    Assert.Equal(-1, AngleOf(be));
  }

  [Fact]
  public void A_rotated_furnace_resolves_its_offsets_into_the_rotated_frame() {
    var world = new TestWorld();
    var north = Furnace(world, 0f);
    var east = Furnace(world, MathF.PI / 2f);

    BlockPos Tuyere(BlockEntityBlastFurnace be) =>
      (BlockPos)ReflectionHelpers.Invoke(be, "GetGlobalPos", 0, -2, 1)!;

    Assert.NotEqual(Tuyere(north), Tuyere(east));
  }
}

using ExpandedLib.Helpers;
using Vintagestory.API.MathTools;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The mod family's horizontal rotation math (north 0, west 90, south 180, east 270). Every oriented
/// block relies on these to place fillers, connectors and particle boxes, so the rotation convention
/// is pinned here once: <c>(x,z) -> 90:(z,-x) -> 180:(-x,-z) -> 270:(-z,x)</c>.
/// </summary>
public class ExOrientationTests {
  [Theory]
  [InlineData("north", 0)]
  [InlineData("n", 0)]
  [InlineData("west", 90)]
  [InlineData("w", 90)]
  [InlineData("south", 180)]
  [InlineData("s", 180)]
  [InlineData("east", 270)]
  [InlineData("e", 270)]
  [InlineData("garbage", 0)]
  [InlineData(null, 0)]
  public void AngleFromSide_maps_both_full_names_and_letter_codes(
    string? side,
    int expected
  ) {
    Assert.Equal(expected, ExOrientation.AngleFromSide(side));
  }

  [Fact]
  public void RotateOffset_leaves_zero_angle_untouched() {
    var r = ExOrientation.RotateOffset(new Vec3i(1, 5, 2), 0);
    Assert.Equal(new Vec3i(1, 5, 2), r);
  }

  [Theory]
  [InlineData(90, 2, -1)] // (x,z)=(1,2) -> (z,-x)
  [InlineData(180, -1, -2)] // -> (-x,-z)
  [InlineData(270, -2, 1)] // -> (-z,x)
  public void RotateOffset_applies_the_convention_and_preserves_y(
    int angle,
    int expectedX,
    int expectedZ
  ) {
    var r = ExOrientation.RotateOffset(new Vec3i(1, 7, 2), angle);
    Assert.Equal(expectedX, r.X);
    Assert.Equal(7, r.Y); // Y axis is never rotated
    Assert.Equal(expectedZ, r.Z);
  }

  [Fact]
  public void RotateOffset_normalizes_out_of_range_angles() {
    // Angle + 180 callers pass values like 450; these must normalize, not fall through to 0.
    Assert.Equal(
      ExOrientation.RotateOffset(new Vec3i(1, 0, 2), 90),
      ExOrientation.RotateOffset(new Vec3i(1, 0, 2), 450)
    );
    Assert.Equal(
      ExOrientation.RotateOffset(new Vec3i(1, 0, 2), 270),
      ExOrientation.RotateOffset(new Vec3i(1, 0, 2), -90)
    );
  }

  [Fact]
  public void RotateOffset_full_turn_is_identity() {
    var off = new Vec3i(3, 1, -4);
    Assert.Equal(off, ExOrientation.RotateOffset(off, 360));
  }

  [Fact]
  public void GlobalPos_adds_the_rotated_offset_to_origin() {
    var origin = new BlockPos(10, 20, 30);
    // local (1,0,2) at 90 deg -> (2,0,-1)
    var p = ExOrientation.GlobalPos(origin, 1, 0, 2, 90);
    Assert.Equal(new BlockPos(12, 20, 29), p);
  }

  [Fact]
  public void GlobalPos_does_not_mutate_the_origin() {
    var origin = new BlockPos(0, 0, 0);
    ExOrientation.GlobalPos(origin, 5, 1, 5, 180);
    Assert.Equal(new BlockPos(0, 0, 0), origin);
  }

  [Fact]
  public void RotateFacing_leaves_vertical_faces_alone() {
    Assert.Equal(
      BlockFacing.UP,
      ExOrientation.RotateFacing(BlockFacing.UP, 90)
    );
    Assert.Equal(
      BlockFacing.DOWN,
      ExOrientation.RotateFacing(BlockFacing.DOWN, 270)
    );
  }

  [Theory]
  [InlineData(0, "north")]
  [InlineData(90, "west")]
  [InlineData(180, "south")]
  [InlineData(270, "east")]
  public void RotateFacing_rotates_north_through_the_compass(
    int angle,
    string expectedCode
  ) {
    var f = ExOrientation.RotateFacing(BlockFacing.NORTH, angle);
    Assert.Equal(expectedCode, f.Code);
  }

  [Fact]
  public void ReadOffset_returns_fallback_for_null_node() {
    var fb = new Vec3i(1, 2, 3);
    Assert.Equal(fb, ExOrientation.ReadOffset(null, fb));
  }

  [Fact]
  public void ReadOffsetD_returns_fallback_for_null_node() {
    var fb = new Vec3d(0.5, 1.5, 2.5);
    Assert.Equal(fb, ExOrientation.ReadOffsetD(null, fb));
  }

  [Fact]
  public void WorldPosFromAttr_uses_fallback_offset_then_rotates() {
    var origin = new BlockPos(0, 0, 0);
    // node absent -> fallback (0,0,2); at 180 deg -> (0,0,-2)
    var p = ExOrientation.WorldPosFromAttr(
      origin,
      null,
      new Vec3i(0, 0, 2),
      180
    );
    Assert.Equal(new BlockPos(0, 0, -2), p);
  }

  [Fact]
  public void RotateBoxes_returns_same_array_at_zero_angle() {
    var boxes = new[] { new Cuboidf(0, 0, 0, 1, 1, 1) };
    Assert.Same(boxes, ExOrientation.RotateBoxes(boxes, 0));
  }

  [Fact]
  public void RotateBoxes_returns_same_array_when_empty() {
    var boxes = System.Array.Empty<Cuboidf>();
    Assert.Same(boxes, ExOrientation.RotateBoxes(boxes, 90));
  }

  [Fact]
  public void RotateBoxes_produces_rotated_copies() {
    var boxes = new[] { new Cuboidf(0f, 0f, 0f, 0.5f, 1f, 1f) };
    var rotated = ExOrientation.RotateBoxes(boxes, 180);
    Assert.NotSame(boxes, rotated);
    // 180 deg about (0.5,*,0.5): x in [0,0.5] -> [0.5,1]; z in [0,1] -> [0,1]
    Assert.Equal(0.5f, rotated[0].X1, 3);
    Assert.Equal(1f, rotated[0].X2, 3);
  }

  [Theory]
  [InlineData(0, 0.25f, 0.75f)]
  [InlineData(90, 0.75f, 0.75f)]
  [InlineData(180, 0.75f, 0.25f)]
  [InlineData(270, 0.25f, 0.25f)]
  public void RotateAroundCenter_matches_the_offset_convention(
    int angle,
    float expectedX,
    float expectedZ
  ) {
    float x = 0.25f;
    float z = 0.75f;
    ExOrientation.RotateAroundCenter(ref x, ref z, angle);
    Assert.Equal(expectedX, x, 3);
    Assert.Equal(expectedZ, z, 3);
  }

  [Fact]
  public void RotateAroundCenter_keeps_the_center_fixed() {
    float x = 0.5f;
    float z = 0.5f;
    ExOrientation.RotateAroundCenter(ref x, ref z, 90);
    Assert.Equal(0.5f, x, 3);
    Assert.Equal(0.5f, z, 3);
  }
}

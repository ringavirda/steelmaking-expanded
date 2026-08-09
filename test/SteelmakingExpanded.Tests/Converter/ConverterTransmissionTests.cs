using ExpandedLib.Testing;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The Bessemer transmission's mechanical-power behavior (was 0% covered): a fixed network endpoint
/// that carries the axle into the converter control. Covers its constant resistance and the
/// orientation seeding (discovery face opposite the placed side, one axle sign per axis so opposite
/// facings on a shaft don't counter-rotate).
/// </summary>
public class ConverterTransmissionTests {
  private static BEBehaviorMPConverterTransmission Behavior(string side) {
    var be = new BlockEntityConverterTransmission {
      Block = TestBlocks.Configure(
        new Block(),
        $"smex:converterbessemertransmission-{side}",
        1,
        ("side", side)
      ),
    };
    return new BEBehaviorMPConverterTransmission(be);
  }

  [Fact]
  public void Resistance_is_the_constant_transmission_drag() {
    Assert.Equal(0.25f, Behavior("north").GetResistance(), 5);
  }

  [Theory]
  [InlineData("north", "south")]
  [InlineData("east", "west")]
  [InlineData("south", "north")]
  [InlineData("west", "east")]
  public void Discovery_face_is_opposite_the_placed_side(
    string side,
    string expectedFace
  ) {
    var mp = Behavior(side);
    mp.SetOrientations();
    Assert.Equal(
      BlockFacing.FromCode(expectedFace),
      mp.OutFacingForNetworkDiscovery
    );
  }

  [Theory]
  [InlineData("north")] // Z axis
  [InlineData("east")] // X axis
  public void Axle_uses_a_single_sign_per_axis(string side) {
    var mp = Behavior(side);
    mp.SetOrientations();
    int[] expected =
      mp.OutFacingForNetworkDiscovery.Axis == EnumAxis.X
        ? [-1, 0, 0]
        : [0, 0, -1];
    Assert.Equal(expected, mp.AxisSign);
  }
}

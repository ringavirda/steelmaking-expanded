using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Every canal endpoint as a chisel target. A clogged mold pedestal could not be chiselled at all:
/// its block derives from the tap's while its block entity derives straight from the canal's, so the
/// chained base call type-checked for a tap block entity and returned false on every click. Each
/// endpoint now calls the shared chisel helper directly.
/// </summary>
public class MoltenEndpointChiselTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem("game:metalbit-iron");
    return world;
  }

  private static Block CanalBlock(string type) =>
    TestBlocks.Configure(
      new Block(),
      $"smex:moltencanal-{type}-ns",
      70,
      ("type", type),
      ("orientation", "ns")
    );

  /// <summary>Loads <paramref name="be"/> with hardened metal - solidified and cool enough to chip out.</summary>
  private static void PrimeSolidified(
    BlockEntityMoltenCanal be,
    TestWorld world,
    int units = 40
  ) {
    var tree = new TreeAttribute();
    tree.SetInt("cellAmount", units);
    tree.SetString("cellMetalType", Iron);
    tree.SetFloat("cellTemperature", 300f); // well under 0.3 x the 1500 melting point
    tree.SetBool("solidified", true);
    be.FromTreeAttributes(tree, world.World);
  }

  private static BlockEntityMoltenCanalMoldPedestal Pedestal(TestWorld world) {
    var be = new BlockEntityMoltenCanalMoldPedestal {
      Pos = new BlockPos(0, 8, 0),
      Block = CanalBlock("moldpedestal"),
    };
    world.Attach(be);
    return be;
  }

  [Fact]
  public void A_clogged_canal_cell_is_a_ready_chisel_target() {
    var world = NewWorld();
    var be = new BlockEntityMoltenCanal {
      Pos = new BlockPos(0, 8, 0),
      Block = CanalBlock("straight"),
    };
    world.Attach(be);
    PrimeSolidified(be, world);

    Assert.True(((IChiselableMolten)be).CanChiselOut);
  }

  [Fact]
  public void A_clogged_tap_is_a_ready_chisel_target() {
    var world = NewWorld();
    var be = new BlockEntityMoltenCanalTap {
      Pos = new BlockPos(0, 8, 0),
      Block = CanalBlock("tap"),
    };
    world.Attach(be);
    PrimeSolidified(be, world);

    Assert.True(((IChiselableMolten)be).CanChiselOut);
  }

  [Fact]
  public void A_clogged_canal_start_is_a_ready_chisel_target() {
    var world = NewWorld();
    var be = new BlockEntityMoltenCanalStart {
      Pos = new BlockPos(0, 8, 0),
      Block = CanalBlock("start"),
    };
    world.Attach(be);
    PrimeSolidified(be, world);

    Assert.True(((IChiselableMolten)be).CanChiselOut);
  }

  [Fact]
  public void A_clogged_mold_pedestal_is_a_ready_chisel_target() {
    var world = NewWorld();
    var be = Pedestal(world);
    PrimeSolidified(be, world);

    var target = (IChiselableMolten)be;
    Assert.True(target.HasChiselableContent);
    Assert.True(target.CanChiselOut);
  }

  [Fact]
  public void Chiselling_a_clogged_pedestal_returns_its_metal_and_reopens_the_cell() {
    var world = NewWorld();
    var be = Pedestal(world);
    PrimeSolidified(be, world, 40);

    ItemStack? recovered = ((IChiselableMolten)be).ChiselOut();

    Assert.NotNull(recovered);
    Assert.Equal("game:metalbit-iron", recovered!.Collectible.Code.ToString());
    Assert.Equal(40 / SmexValues.MoltenUnitsPerBit, recovered.StackSize);
    Assert.False(be.Solidified);
    Assert.True(be.IsCellEmpty);
  }

  [Fact]
  public void The_pedestal_block_entity_is_not_a_tap_block_entity() {
    // The premise behind the fix: the pedestal cannot reach the tap's interaction handler, so its
    // block calls the shared chisel helper directly rather than chaining to base.
    Assert.False(
      typeof(BlockEntityMoltenCanalTap).IsAssignableFrom(
        typeof(BlockEntityMoltenCanalMoldPedestal)
      )
    );
  }
}

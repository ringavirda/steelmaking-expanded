using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The molten-canal start acts as the network's <see cref="ILiquidMetalSink"/>: a tap or crucible
/// pours liquid metal here and it flows down the run. This covers the acceptance predicates, the
/// server-side fill (including the "soak heat when full" path that keeps a fed start from plugging)
/// and the pour-tally that drives the block info readout.
/// </summary>
public class MoltenCanalStartTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem("game:ingot-copper", 1084f);
    return world;
  }

  private static BlockEntityMoltenCanalStart Start(TestWorld world) {
    var be = new BlockEntityMoltenCanalStart {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanalstart-ns",
        2,
        ("type", "start"),
        ("orientation", "ns")
      ),
    };
    world.Attach(be);
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  #region Capacity / predicates

  [Fact]
  public void Start_has_double_the_default_canal_capacity() {
    var world = NewWorld();
    Assert.Equal(
      SmexValues.CanalDefaultUnitCapacity * 2,
      Start(world).MaxUnitCapacity
    );
  }

  [Fact]
  public void CanReceiveAny_is_true_while_below_capacity_and_not_solid() {
    var world = NewWorld();
    var be = Start(world);
    Assert.True(be.CanReceiveAny);
  }

  [Fact]
  public void CanReceive_requires_a_matching_metal_once_filled() {
    var world = NewWorld();
    var be = Start(world);
    be.PushMetal(10, Metal(world, Iron, 1400f), world.World);

    Assert.True(be.CanReceive(Metal(world, Iron, 1400f)));
    Assert.False(be.CanReceive(Metal(world, "game:ingot-copper", 1200f)));
  }

  [Fact]
  public void CanReceiveOrSoak_stays_true_for_the_same_metal_even_when_full() {
    var world = NewWorld();
    var be = Start(world);
    be.PushMetal(be.MaxUnitCapacity, Metal(world, Iron, 1400f), world.World);

    // Brim full: CanReceive is false (no room) but the looser soak predicate stays true so a
    // furnace tap keeps bathing it in heat instead of letting it plug.
    Assert.False(be.CanReceive(Metal(world, Iron, 1400f)));
    Assert.True(be.CanReceiveOrSoak(Metal(world, Iron, 1400f)));
    Assert.False(be.CanReceiveOrSoak(Metal(world, "game:ingot-copper", 1200f)));
  }

  #endregion

  #region ReceiveLiquidMetal

  [Fact]
  public void ReceiveLiquidMetal_fills_the_cell_and_consumes_the_poured_amount() {
    var world = NewWorld();
    var be = Start(world);

    int amount = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1400f), ref amount, 1400f);

    Assert.Equal(0, amount); // all 20 fit
    Assert.Equal(20, be.CellAmount);
    Assert.Equal(Iron, be.CellMetalType);
  }

  [Fact]
  public void ReceiveLiquidMetal_leaves_overflow_in_the_pour_amount() {
    var world = NewWorld();
    var be = Start(world);
    int cap = be.MaxUnitCapacity;

    int amount = cap + 30;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1400f), ref amount, 1400f);

    Assert.Equal(cap, be.CellAmount);
    Assert.Equal(30, amount); // the overflow stays with the caller
  }

  [Fact]
  public void ReceiveLiquidMetal_rejects_a_different_metal() {
    var world = NewWorld();
    var be = Start(world);
    be.PushMetal(10, Metal(world, Iron, 1400f), world.World);

    int amount = 20;
    be.ReceiveLiquidMetal(
      Metal(world, "game:ingot-copper", 1200f),
      ref amount,
      1200f
    );

    Assert.Equal(20, amount); // nothing taken
    Assert.Equal(10, be.CellAmount);
    Assert.Equal(Iron, be.CellMetalType);
  }

  [Fact]
  public void ReceiveLiquidMetal_soaks_heat_into_a_full_cell_without_growing_it() {
    var world = NewWorld();
    var be = Start(world);
    be.PushMetal(be.MaxUnitCapacity, Metal(world, Iron, 1200f), world.World);

    int amount = 10; // nowhere to go, but it's hotter than what's there
    be.ReceiveLiquidMetal(Metal(world, Iron, 1490f), ref amount, 1490f);

    Assert.Equal(be.MaxUnitCapacity, be.CellAmount); // volume unchanged
    Assert.Equal(1490f, be.CellTemperature, 0); // heat soaked in
    Assert.Equal(10, amount); // overflow returned to caller
  }

  #endregion

  #region Pour tally serialization

  [Fact]
  public void Pour_tally_round_trips_through_the_tree() {
    var world = NewWorld();
    var be = Start(world);

    int amount = 25;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1400f), ref amount, 1400f);

    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);
    Assert.Equal(25, tree.GetInt("pourTally"));

    var restored = Start(world);
    restored.FromTreeAttributes(tree, world.World);
    var roundTrip = new TreeAttribute();
    restored.ToTreeAttributes(roundTrip);
    Assert.Equal(25, roundTrip.GetInt("pourTally"));
  }

  #endregion
}

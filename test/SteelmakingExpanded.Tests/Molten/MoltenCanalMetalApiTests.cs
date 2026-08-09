using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The per-cell metal API on <see cref="BlockEntityMoltenCanal"/>: pushing, draining, heat-soaking,
/// the per-tick thermal update that latches a cell solid, and the solidified-drop recovery. These
/// are the production paths the network drives every tick, so they are exercised against a world that
/// resolves real temperature-tracked item stacks (iron melts at 1500 °C here).
/// </summary>
public class MoltenCanalMetalApiTests {
  private const string Iron = "game:ingot-iron";
  private const float IronMelt = 1500f;

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, IronMelt);
    world.RegisterItem("game:metalbit-iron"); // solidified-drop target
    world.RegisterItem("game:ingot-copper", 1084f);
    return world;
  }

  private static BlockEntityMoltenCanal Canal(TestWorld world) {
    var be = new BlockEntityMoltenCanal {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanal-straight-ns",
        1,
        ("type", "straight"),
        ("orientation", "ns")
      ),
    };
    world.Attach(be);
    return be;
  }

  /// <summary>A temperature-tracked metal stack the way a pouring tap/crucible hands one over.</summary>
  private static ItemStack Metal(TestWorld world, string code, float temp) {
    var stack = MoltenMetal.CreateStack(world.World, code, temp)!;
    Assert.NotNull(stack);
    return stack;
  }

  // SoakHeat / UpdateThermal / EnsureMetalStack are internal (driven by MoltenNetwork.OnTick in
  // production). Reach them through the harness's reflection shim - the repo convention rather than
  // opening up internals on the shipped assembly.
  private static bool SoakHeat(
    BlockEntityMoltenCanal be,
    TestWorld world,
    float temp
  ) => (bool)ReflectionHelpers.Invoke(be, "SoakHeat", world.World, temp)!;

  private static void UpdateThermal(
    BlockEntityMoltenCanal be,
    TestWorld world
  ) => ReflectionHelpers.Invoke(be, "UpdateThermal", world.World);

  private static void EnsureMetalStack(
    BlockEntityMoltenCanal be,
    TestWorld world
  ) => ReflectionHelpers.Invoke(be, "EnsureMetalStack", world.World);

  #region PushMetal

  [Fact]
  public void PushMetal_fills_an_empty_cell_with_type_and_temperature() {
    var world = NewWorld();
    var be = Canal(world);

    int accepted = be.PushMetal(20, Metal(world, Iron, 1400f), world.World);

    Assert.Equal(20, accepted);
    Assert.Equal(20, be.CellAmount);
    Assert.Equal(Iron, be.CellMetalType);
    Assert.Equal(1400f, be.CellTemperature, 1);
  }

  [Fact]
  public void PushMetal_clamps_to_capacity_and_reports_only_what_fit() {
    var world = NewWorld();
    var be = Canal(world);
    int cap = be.MaxUnitCapacity;

    int accepted = be.PushMetal(
      cap + 50,
      Metal(world, Iron, 1400f),
      world.World
    );

    Assert.Equal(cap, accepted);
    Assert.Equal(cap, be.CellAmount);
  }

  [Fact]
  public void PushMetal_rejects_a_different_metal_type() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(10, Metal(world, Iron, 1400f), world.World);

    int accepted = be.PushMetal(
      10,
      Metal(world, "game:ingot-copper", 1200f),
      world.World
    );

    Assert.Equal(0, accepted);
    Assert.Equal(10, be.CellAmount);
    Assert.Equal(Iron, be.CellMetalType);
  }

  [Fact]
  public void PushMetal_volume_weights_the_blended_temperature() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(10, Metal(world, Iron, 1000f), world.World);
    be.PushMetal(30, Metal(world, Iron, 1400f), world.World);

    // (10*1000 + 30*1400) / 40 = 1300
    Assert.Equal(40, be.CellAmount);
    Assert.Equal(1300f, be.CellTemperature, 0);
  }

  #endregion

  #region DrainMetal

  [Fact]
  public void DrainMetal_removes_a_partial_amount_and_keeps_the_metal() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(40, Metal(world, Iron, 1400f), world.World);

    int drained = be.DrainMetal(15);

    Assert.Equal(15, drained);
    Assert.Equal(25, be.CellAmount);
    Assert.Equal(Iron, be.CellMetalType);
  }

  [Fact]
  public void DrainMetal_emptying_a_cell_clears_its_metal_type() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);

    int drained = be.DrainMetal(999);

    Assert.Equal(20, drained);
    Assert.True(be.IsCellEmpty);
    Assert.Equal("", be.CellMetalType);
    Assert.Equal(0, be.GlowLightLevel);
  }

  #endregion

  #region SoakHeat

  [Fact]
  public void SoakHeat_raises_temperature_without_adding_volume() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, 1300f), world.World);

    bool soaked = SoakHeat(be, world, 1450f);

    Assert.True(soaked);
    Assert.Equal(20, be.CellAmount);
    Assert.Equal(1450f, be.CellTemperature, 0);
  }

  [Fact]
  public void SoakHeat_does_nothing_when_incoming_is_not_hotter() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);

    Assert.False(SoakHeat(be, world, 1400f));
    Assert.False(SoakHeat(be, world, 1000f));
  }

  [Fact]
  public void SoakHeat_does_nothing_for_an_empty_cell() {
    var world = NewWorld();
    Assert.False(SoakHeat(Canal(world), world, 1500f));
  }

  #endregion

  #region UpdateThermal

  [Fact]
  public void UpdateThermal_latches_solidified_below_the_melting_point() {
    var world = NewWorld();
    var be = Canal(world);
    // Poured in below iron's 1500 melt point -> next thermal tick should latch it solid.
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    Assert.False(be.Solidified);

    UpdateThermal(be, world);

    Assert.True(be.Solidified);
    Assert.True(be.IsConnectionBroken());
  }

  [Fact]
  public void UpdateThermal_keeps_a_cell_above_the_melting_point_liquid() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, 1600f), world.World);

    UpdateThermal(be, world);

    Assert.False(be.Solidified);
    Assert.True(be.HasMoltenMetal);
  }

  #endregion

  #region CellState / IsHardened

  [Theory]
  [InlineData(1400f, MoltenState.Liquid)] // >= 0.8 * 1500
  [InlineData(900f, MoltenState.Cooling)] // between 0.3 and 0.8
  [InlineData(300f, MoltenState.Hardened)] // < 0.3 * 1500
  public void CellState_classifies_against_the_melting_point(
    float temp,
    MoltenState expected
  ) {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, temp), world.World);

    Assert.Equal(expected, be.CellState);
    Assert.Equal(expected == MoltenState.Hardened, be.IsHardened);
  }

  [Fact]
  public void CellState_reads_liquid_for_an_empty_cell() {
    var world = NewWorld();
    Assert.Equal(MoltenState.Liquid, Canal(world).CellState);
  }

  #endregion

  #region Solidified drop

  [Fact]
  public void GetSolidifiedDrop_yields_metalbits_scaled_by_amount() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, 300f), world.World);
    UpdateThermal(be, world); // hardens it

    var drop = be.GetSolidifiedDrop(world.World);

    Assert.NotNull(drop);
    Assert.Equal("game:metalbit-iron", drop!.Collectible.Code.ToString());
    Assert.Equal(20 / 5, drop.StackSize); // 1 bit per 5 units
  }

  [Fact]
  public void GetSolidifiedDrop_is_null_while_still_liquid() {
    var world = NewWorld();
    var be = Canal(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);

    Assert.Null(be.GetSolidifiedDrop(world.World));
  }

  #endregion

  #region Re-use: refill after chiselling out a plug

  // The cowper lesson generalized to the canal: a cell's life cycle is pour → harden → chisel out →
  // pour again. Every other test stops at one of those steps; none crosses chisel→refill, where a
  // stale Solidified latch or leftover temperature from the first plug could poison the refill.
  [Fact]
  public void A_chiselled_out_cell_accepts_a_fresh_pour_and_rejoins_the_run() {
    var world = NewWorld();
    var be = Canal(world);

    // Pour iron in cold, let it harden, then chisel it out (the solidify→chisel half of the cycle).
    be.PushMetal(20, Metal(world, Iron, 300f), world.World);
    UpdateThermal(be, world);
    Assert.True(be.Solidified);
    Assert.True(be.IsConnectionBroken()); // a hardened plug severs the run

    Assert.NotNull(be.ClearSolidified());
    Assert.False(be.Solidified);
    Assert.True(be.IsCellEmpty);

    // Refill the chiselled cell: it must accept fresh molten metal at the NEW pour's temperature
    // (no blend with the cleared plug's residue) and rejoin the network.
    int accepted = be.PushMetal(20, Metal(world, Iron, 1450f), world.World);

    Assert.Equal(20, accepted);
    Assert.Equal(1450f, be.CellTemperature, 0); // fresh temperature, not blended with the old plug
    Assert.False(be.IsConnectionBroken()); // connectivity restored
    Assert.True(be.HasMoltenMetal);
  }

  // A chiselled cell may even be repurposed for a DIFFERENT metal - the type guard keys off the live
  // cell content, which the chisel cleared, so the old type must not linger and reject the new pour.
  [Fact]
  public void A_chiselled_out_cell_accepts_a_different_metal() {
    var world = NewWorld();
    var be = Canal(world);

    be.PushMetal(20, Metal(world, Iron, 300f), world.World);
    UpdateThermal(be, world);
    be.ClearSolidified();

    int accepted = be.PushMetal(
      20,
      Metal(world, "game:ingot-copper", 1200f),
      world.World
    );

    Assert.Equal(20, accepted);
    Assert.Equal("game:ingot-copper", be.CellMetalType);
  }

  #endregion

  #region EnsureMetalStack (post-load rebuild)

  [Fact]
  public void EnsureMetalStack_rebuilds_the_carrier_so_thermal_runs_after_load() {
    var world = NewWorld();
    var be = Canal(world);

    // Simulate a fresh load: state arrives through the tree, no live carrier yet.
    var tree = new TreeAttribute();
    tree.SetInt("cellAmount", 20);
    tree.SetString("cellMetalType", Iron);
    tree.SetFloat("cellTemperature", 1400f);
    be.FromTreeAttributes(tree, world.World);

    EnsureMetalStack(be, world);
    UpdateThermal(be, world);

    // With the carrier rebuilt, the below-melt cell latches solid on the first tick.
    Assert.True(be.Solidified);
  }

  #endregion
}

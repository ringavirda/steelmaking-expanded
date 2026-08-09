using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockStructures.Converter;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The Bessemer converter's charge-handling process, exercised directly (the full
/// <c>OnProductionTick</c> is gated on four aligned peripherals + a constructed vessel, but the per-
/// state steps are reachable on their own): filling from the input canal cell, pouring into the
/// output cell, refining iron into steel, and latching solid below the melting point. Plus the
/// player-facing operability gate, which must refuse an unbuilt converter.
/// </summary>
public class ConverterControlProcessTests {
  private const string Iron = "game:ingot-iron";
  private const string Steel = "game:ingot-steel";

  // Structure-local peripheral offsets (mirrors the private constants in the control).
  private static readonly (int x, int y, int z) InputTapLocal = (1, 1, 2);
  private static readonly (int x, int y, int z) OutputStartLocal = (1, -2, 2);

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem(Steel, 1500f);
    world.RegisterItem("game:metalbit-iron");
    return world;
  }

  private static BlockEntityConverterControl Control(TestWorld world) {
    var be = new BlockEntityConverterControl {
      Pos = new BlockPos(0, 8, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:converterbessemercontrol-north",
        1,
        ("side", "north")
      ),
    };
    world.Attach(be);
    // Establish the structure angle so the peripheral offsets resolve (Initialize would do this).
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation");
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  /// <summary>Places a molten-canal cell at the control's resolved peripheral offset and returns it.</summary>
  private static BlockEntityMoltenCanal PlaceCell(
    TestWorld world,
    BlockEntityConverterControl control,
    (int x, int y, int z) local
  ) {
    var pos = (BlockPos)
      ReflectionHelpers.Invoke(
        control,
        "GetGlobalPos",
        local.x,
        local.y,
        local.z
      )!;
    var cell = new BlockEntityMoltenCanal {
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanal-straight-ns",
        9,
        ("type", "straight"),
        ("orientation", "ns")
      ),
    };
    world.Place(pos, cell.Block, cell);
    world.Attach(cell);
    return cell;
  }

  #region Filling

  [Fact]
  public void TickFilling_draws_molten_metal_from_the_input_cell() {
    var world = NewWorld();
    var be = Control(world);
    var input = PlaceCell(world, be, InputTapLocal);
    input.PushMetal(50, Metal(world, Iron, 1400f), world.World);

    ReflectionHelpers.Invoke(be, "TickFilling", 1f);

    Assert.Equal(50, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
    Assert.True(input.IsCellEmpty); // drained into the vessel
  }

  [Fact]
  public void TickFilling_does_nothing_when_the_input_cell_is_empty() {
    var world = NewWorld();
    var be = Control(world);
    PlaceCell(world, be, InputTapLocal); // present but empty

    ReflectionHelpers.Invoke(be, "TickFilling", 1f);

    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
  }

  #endregion

  #region Pouring

  [Fact]
  public void TickPouring_pushes_the_charge_into_the_output_cell() {
    var world = NewWorld();
    var be = Control(world);
    var output = PlaceCell(world, be, OutputStartLocal);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, 1400f));
    ReflectionHelpers.SetField(be, "_contentUnits", 40);

    ReflectionHelpers.Invoke(be, "TickPouring", 1f);

    Assert.True(output.CellAmount > 0);
    Assert.Equal(Iron, output.CellMetalType);
    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
  }

  #endregion

  #region Refining

  [Fact]
  public void CompleteRefining_turns_the_iron_charge_into_steel() {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, 1400f));
    ReflectionHelpers.SetField(be, "_contentUnits", 50);

    ReflectionHelpers.Invoke(be, "CompleteRefining");

    var content = (ItemStack)ReflectionHelpers.GetField(be, "_content")!;
    Assert.Equal(Steel, content.Collectible.Code.ToString());
  }

  #endregion

  #region Solidify latch

  [Theory]
  [InlineData(1600f, false)] // above the 1500 melt point -> stays liquid
  [InlineData(300f, true)] // below melt -> latches solid
  public void UpdateSolidified_latches_against_the_melting_point(
    float temp,
    bool expected
  ) {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, temp));
    ReflectionHelpers.SetField(be, "_contentUnits", 30);

    ReflectionHelpers.Invoke(be, "UpdateSolidified");

    Assert.Equal(
      expected,
      (bool)ReflectionHelpers.GetField(be, "_solidified")!
    );
  }

  #endregion

  #region Operability gate

  [Fact]
  public void CanOperate_refuses_an_incomplete_structure_with_a_reason() {
    var world = NewWorld();
    var be = Control(world);

    bool ok = be.CanOperate(out string error);

    Assert.False(ok);
    Assert.NotEqual("", error);
  }

  [Fact]
  public void IsConverterPresent_is_false_with_no_vessel_placed() {
    var world = NewWorld();
    Assert.False(Control(world).IsConverterPresent());
  }

  #endregion
}

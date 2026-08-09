using ExpandedLib.Testing;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using ModBoiler = PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntityBoiler;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The reusable fakes that make a boiler's gated production tick run headlessly: a finished
/// right-click construction and a burning firebox. Shared by the unit-level <see cref="BoilerRig"/>
/// and the integration-level <c>BoilerFixture</c>, so the wiring lives in one place.
/// </summary>
internal static class BoilerFakes {
  /// <summary>
  /// Forces <paramref name="be"/> to read as fully constructed and structure-complete. Order matters
  /// when the entity will also be Initialized: a real <c>Initialize</c> re-reads <c>_rcc</c> from the
  /// (absent) behaviors and would clear it, so callers that Initialize must call this again afterwards.
  /// </summary>
  public static void ForceConstructed(ModBoiler be) {
    RccFake.Complete(be);
    ReflectionHelpers.SetProperty(be, "StructureComplete", true);
  }

  /// <summary>A lit coal pile holding fuel, for the firebox cell.</summary>
  public static BlockEntityCoalPile BurningPile(BlockPos pos) {
    var pile = new BlockEntityCoalPile { Pos = pos.Copy() };
    var inv = new InventoryGeneric(1, "coalpile", "test", null, null);
    inv[0].Itemstack = new ItemStack(
      TestBlocks.Configure(new Block(), "game:coal", 3)
    );
    ReflectionHelpers.SetField(pile, "inventory", inv);
    ReflectionHelpers.SetField(pile, "burning", true);
    return pile;
  }
}

using System;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The canal tap drains the network's metal into a parked barrel or mold while pouring is enabled,
/// and severs the run when closed. This covers the capacity, the open/closed connectivity gate,
/// the barrel attach/detach content round trip, and the per-tick drain (capacity- and type-gated).
/// </summary>
public class MoltenCanalTapTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem("game:ingot-copper", 1084f);
    // The barrel block the tap resolves when detaching a parked barrel.
    world.Register(TestBlocks.Configure(new Block(), "smex:moltenbarrel", 50));
    return world;
  }

  private static BlockEntityMoltenCanalTap Tap(TestWorld world) {
    var be = new BlockEntityMoltenCanalTap {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(
        new Block(),
        "smex:moltencanaltap-ns",
        3,
        ("type", "tap"),
        ("orientation", "ns")
      ),
    };
    world.Attach(be);
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  private static void OpenTap(BlockEntityMoltenCanalTap be) =>
    be.TryTogglePouring(); // default is closed; flips to pouring

  private static void ServerTick(BlockEntityMoltenCanalTap be) =>
    ReflectionHelpers.Invoke(be, "OnServerTick", 1f);

  #region Capacity / connectivity

  [Fact]
  public void Tap_capacity_is_half_the_default_rounded_up() {
    var world = NewWorld();
    Assert.Equal(
      (int)Math.Ceiling(SmexValues.CanalDefaultUnitCapacity / 2.0),
      Tap(world).MaxUnitCapacity
    );
  }

  [Fact]
  public void A_closed_tap_severs_the_run_an_open_one_does_not() {
    var world = NewWorld();
    var be = Tap(world);

    Assert.False(be.IsPouring);
    Assert.True(be.IsConnectionBroken()); // closed -> severed leaf

    OpenTap(be);
    Assert.True(be.IsPouring);
    Assert.False(be.IsConnectionBroken());
  }

  [Fact]
  public void TryTogglePouring_flips_the_pour_state() {
    var world = NewWorld();
    var be = Tap(world);

    be.TryTogglePouring();
    Assert.True(be.IsPouring);
    be.TryTogglePouring();
    Assert.False(be.IsPouring);
  }

  #endregion

  #region Barrel attach / detach

  [Fact]
  public void AddBarrel_adopts_the_stacks_metal_and_capacity() {
    var world = NewWorld();
    var be = Tap(world);

    // A barrel item carrying 20 units of iron in its block-entity attributes.
    var barrelStack = new ItemStack(
      world.World.GetBlock(new AssetLocation("smex:moltenbarrel"))
    );
    MoltenContents.Write(
      barrelStack,
      MoltenContents.BarrelUnitsKey,
      Metal(world, Iron, 1300f),
      20
    );

    be.AddBarrel(barrelStack);

    Assert.True(be.IsBarrel);
    Assert.False(be.IsMold);
    Assert.True(be.HasContent);
    Assert.Equal(20, be.BarrelCurrentUnits);
    Assert.Equal(BlockMoltenBarrel.MaxUnits, be.BarrelMaxUnits);
    Assert.NotNull(be.BarrelMetalContent);
  }

  [Fact]
  public void RemoveBarrel_returns_a_stack_carrying_the_preserved_contents() {
    var world = NewWorld();
    var be = Tap(world);
    var barrelStack = new ItemStack(
      world.World.GetBlock(new AssetLocation("smex:moltenbarrel"))
    );
    MoltenContents.Write(
      barrelStack,
      MoltenContents.BarrelUnitsKey,
      Metal(world, Iron, 1300f),
      20
    );
    be.AddBarrel(barrelStack);

    var removed = be.RemoveBarrel();

    Assert.False(be.IsBarrel);
    Assert.Equal(0, be.BarrelCurrentUnits);
    var (content, units) = MoltenContents.Read(
      removed,
      MoltenContents.BarrelUnitsKey,
      world.World
    );
    Assert.Equal(20, units);
    Assert.NotNull(content);
  }

  #endregion

  #region Server-side drain

  // The drain speed is read live from the block's "drainSpeed" attribute (config fallback otherwise),
  // so prime it by setting that attribute on the test block.
  private static void PrimeDrainSpeed(
    BlockEntityMoltenCanalTap be,
    float speed
  ) =>
    be.Block.Attributes = new JsonObject(
      new JObject { ["drainSpeed"] = speed }
    );

  [Fact]
  public void OnServerTick_drains_cell_metal_into_a_parked_barrel() {
    var world = NewWorld();
    var be = Tap(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    be.IsBarrel = true;
    PrimeDrainSpeed(be, 10f);
    OpenTap(be);

    ServerTick(be);

    Assert.Equal(10, be.BarrelCurrentUnits); // one drain step
    Assert.Equal(10, be.CellAmount); // pulled out of the cell
    Assert.NotNull(be.BarrelMetalContent);
  }

  [Fact]
  public void OnServerTick_does_nothing_while_the_tap_is_closed() {
    var world = NewWorld();
    var be = Tap(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    be.IsBarrel = true;
    PrimeDrainSpeed(be, 10f);
    // not opened -> not pouring

    ServerTick(be);

    Assert.Equal(0, be.BarrelCurrentUnits);
    Assert.Equal(20, be.CellAmount);
  }

  [Fact]
  public void OnServerTick_will_not_mix_a_different_metal_into_the_barrel() {
    var world = NewWorld();
    var be = Tap(world);
    be.PushMetal(20, Metal(world, Iron, 1400f), world.World);
    be.IsBarrel = true;
    // Barrel already holds copper - the iron in the cell must not be drained into it.
    ReflectionHelpers.SetProperty(
      be,
      nameof(be.BarrelMetalContent),
      Metal(world, "game:ingot-copper", 1100f)
    );
    ReflectionHelpers.SetProperty(be, nameof(be.BarrelCurrentUnits), 5);
    PrimeDrainSpeed(be, 10f);
    OpenTap(be);

    ServerTick(be);

    Assert.Equal(5, be.BarrelCurrentUnits); // unchanged
    Assert.Equal(20, be.CellAmount);
  }

  #endregion

  #region Cell solidify + chisel-clear

  // The tap's own cell now clogs and is chiselled clear like a canal or the start block, instead of
  // staying permanently liquid - so a run that goes cold with metal left in the tap can be recovered.
  [Fact]
  public void A_cold_tap_cell_solidifies_severs_and_can_be_cleared() {
    var world = NewWorld();
    world.RegisterItem("game:metalbit-iron"); // the chiselled-out solid drop
    var be = Tap(world);
    be.PushMetal(20, Metal(world, Iron, 400f), world.World); // below the 1500 melting point

    ReflectionHelpers.Invoke(be, "UpdateThermal", world.World);

    Assert.True(be.Solidified);
    Assert.True(be.IsConnectionBroken()); // clogged -> drops off the run

    Assert.NotNull(be.ClearSolidified()); // chiselled clear
    Assert.False(be.Solidified);
  }

  #endregion

  #region Serialization

  [Fact]
  public void Tap_pour_and_content_flags_round_trip_through_the_tree() {
    var world = NewWorld();
    var src = Tap(world);
    OpenTap(src);
    src.IsBarrel = true;
    ReflectionHelpers.SetProperty(src, nameof(src.BarrelCurrentUnits), 15);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var restored = Tap(world);
    restored.FromTreeAttributes(tree, world.World);

    Assert.True(restored.IsPouring);
    Assert.True(restored.IsBarrel);
    Assert.Equal(15, restored.BarrelCurrentUnits);
  }

  #endregion
}

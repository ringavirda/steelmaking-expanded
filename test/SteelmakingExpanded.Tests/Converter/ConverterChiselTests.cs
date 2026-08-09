using System.Linq;
using ExpandedLib.Testing;
using NSubstitute;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using SteelmakingExpanded.BlockStructures.Converter.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The player-relief mechanics added after the first Bessemer playtest: the charge cools slower
/// inside the vessel (a configurable coefficient on the molten cooldown speed, so a finished heat
/// gives more time to pour), and a small fully-hardened residue that solidified mid-pour can be
/// chiselled out instead of breaking the whole - expensive - converter.
/// </summary>
public class ConverterChiselTests {
  private const string Iron = "game:ingot-iron";
  private const string Steel = "game:ingot-steel";

  // Iron melts at 1500: hardened below 0.3x = 450, liquid above 0.8x = 1200. Capacity 1200, so the
  // 20% chisel ceiling is 240 units.
  private const float IronMelt = 1500f;

  private static readonly (int x, int y, int z) InputTapLocal = (1, 1, 2);

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, IronMelt);
    world.RegisterItem(Steel, IronMelt);
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
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation");
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  // The VS time-based cooldown speed lives on the stack's temperature tree.
  private static float CooldownSpeedOf(ItemStack stack) =>
    (stack.Attributes["temperature"] as ITreeAttribute)?.GetFloat(
      "cooldownSpeed"
    ) ?? 0f;

  // Primes the control's charge directly (bypasses the peripheral-gated tick).
  private static void PrimeCharge(
    BlockEntityConverterControl be,
    TestWorld world,
    float temp,
    int units,
    bool solidified
  ) {
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, temp));
    ReflectionHelpers.SetField(be, "_contentUnits", units);
    ReflectionHelpers.SetField(be, "_solidified", solidified);
  }

  private static float ExpectedSlowedCooldown =>
    SmexValues.MoltenCooldownSpeed * SmexValues.BessemerCooldownCoefficient;

  #region Cooldown coefficient

  [Fact]
  public void Filling_creates_the_charge_with_the_slowed_converter_cooldown() {
    var world = NewWorld();
    var be = Control(world);
    var input = PlaceInputCell(world, be);
    input.PushMetal(50, Metal(world, Iron, 1400f), world.World);

    ReflectionHelpers.Invoke(be, "TickFilling", 1f);

    var content = (ItemStack)ReflectionHelpers.GetField(be, "_content")!;
    Assert.Equal(ExpectedSlowedCooldown, CooldownSpeedOf(content), 3);
  }

  [Fact]
  public void Refined_steel_carries_the_slowed_converter_cooldown() {
    var world = NewWorld();
    var be = Control(world);
    ReflectionHelpers.SetField(be, "_content", Metal(world, Iron, 1700f));
    ReflectionHelpers.SetField(be, "_contentUnits", 50);

    ReflectionHelpers.Invoke(be, "CompleteRefining");

    var content = (ItemStack)ReflectionHelpers.GetField(be, "_content")!;
    Assert.Equal(Steel, content.Collectible.Code.ToString());
    Assert.Equal(ExpectedSlowedCooldown, CooldownSpeedOf(content), 3);
  }

  [Fact]
  public void The_default_coefficient_halves_the_molten_cooldown_speed() {
    // 0.5 x the molten-system rate, the spec'd default (cools twice as slowly inside the vessel).
    Assert.Equal(0.5f, SmexValues.BessemerCooldownCoefficient, 3);
    Assert.Equal(
      SmexValues.MoltenCooldownSpeed * 0.5f,
      ExpectedSlowedCooldown,
      3
    );
  }

  // Regression (player-reported): /exmod config smex bessemercooldowncoefficient 10 did nothing,
  // because the rate was baked into the charge only when it was first poured. The tick now re-stamps
  // the live rate, so a coefficient change reaches the metal already in the vessel.
  [Fact]
  public void Changing_the_cooldown_coefficient_reaches_the_charge_already_in_the_vessel() {
    var world = NewWorld();
    var be = Control(world);
    var content = Metal(world, Iron, 1400f);
    ReflectionHelpers.SetField(be, "_content", content);
    ReflectionHelpers.SetField(be, "_contentUnits", 50);

    float original = SmexValues.BessemerCooldownCoefficient;
    try {
      // Admin speeds the cooling way up mid-session.
      SmexValues.Edit(c => c.BessemerCooldownCoefficient = 10f);
      ReflectionHelpers.Invoke(be, "SyncContentCooldown");

      Assert.Equal(
        SmexValues.MoltenCooldownSpeed * 10f,
        CooldownSpeedOf(content),
        3
      );
    } finally {
      SmexValues.Edit(c => c.BessemerCooldownCoefficient = original);
    }
  }

  #endregion

  #region Chisel-out gating

  [Fact]
  public void A_small_hardened_residue_can_be_chiselled_out() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 300f, 100, solidified: true); // hardened (300<450), 100 < 240

    Assert.True(be.CanChiselOut());
  }

  [Fact]
  public void A_solidified_but_still_hot_residue_cannot_be_chiselled() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 800f, 100, solidified: true); // 450 < 800 < 1500 -> cooling, not hardened

    Assert.False(be.ChargeIsHardened);
    Assert.False(be.CanChiselOut());
  }

  [Fact]
  public void A_large_hardened_charge_cannot_be_chiselled_only_broken() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 300f, 300, solidified: true); // hardened but 300 >= 240 (20% of 1200)

    Assert.True(be.ChargeIsHardened);
    Assert.False(be.CanChiselOut());
  }

  [Fact]
  public void A_still_liquid_charge_cannot_be_chiselled() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 300f, 100, solidified: false);

    Assert.False(be.HasSolidifiedCharge);
    Assert.False(be.CanChiselOut());
  }

  #endregion

  #region Self-drop suppression

  // The vessel is spawned by the control block, never placed from an item, so there is no vessel item
  // to hand back - dropping itself would mint a block the player could not have crafted. "drops": []
  // in the JSON is not always honoured for a variant block, so the block overrides GetDrops. Simulate
  // the registry handing the block its own code as a fallback drop and confirm the override strips it.
  [Fact]
  public void Bessemer_vessel_never_drops_itself_even_if_registered_with_a_self_drop() {
    var world = NewWorld();
    var block = TestBlocks.Configure(
      new BlockConverterBessemer(),
      "smex:converterbessemer-north",
      1,
      ("side", "north")
    );
    block.Drops = [new BlockDropItemStack(new ItemStack(block))];

    ItemStack[] drops = block.GetDrops(
      world.World,
      new BlockPos(0, 8, 0),
      null
    );

    Assert.DoesNotContain(drops, d => d.Collectible == block);
  }

  // What the vessel does return is the placement cost the control block took from the player's hotbar
  // - the large gear and the rods - so breaking one to move it is not a net loss.
  [Fact]
  public void Bessemer_vessel_returns_the_gear_and_rods_it_was_placed_with() {
    var world = NewWorld();
    var gear = new Item { Code = new AssetLocation("ppex:largegear-iron") };
    var rod = new Item { Code = new AssetLocation("game:rod-iron") };
    world.World.GetItem(new AssetLocation("ppex:largegear-iron")).Returns(gear);
    world.World.GetItem(new AssetLocation("game:rod-iron")).Returns(rod);

    var block = TestBlocks.Configure(
      new BlockConverterBessemer(),
      "smex:converterbessemer-north",
      1,
      ("side", "north")
    );

    ItemStack[] drops = block.GetDrops(
      world.World,
      new BlockPos(0, 8, 0),
      null
    );

    Assert.Equal(
      SmexValues.BessemerRequiredGears,
      drops.Single(d => d.Collectible == gear).StackSize
    );
    Assert.Equal(
      SmexValues.BessemerRequiredRods,
      drops.Single(d => d.Collectible == rod).StackSize
    );
  }

  #endregion

  #region Solidified status feedback

  // The block-info status walks the player through clearing a frozen charge, the way a clogged canal
  // does: break a large residue, wait for a small-but-hot one to harden, chisel a small hardened one.
  private static string SolidifiedStatus(BlockEntityConverterControl be) =>
    (string)ReflectionHelpers.Invoke(be, "SolidifiedStatus")!;

  [Fact]
  public void Status_tells_the_player_to_break_a_large_solidified_charge() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 300f, 300, solidified: true); // hardened but 300 >= 240

    Assert.Equal("smex:bessemer-status-solidified", SolidifiedStatus(be));
  }

  [Fact]
  public void Status_tells_the_player_to_wait_for_a_small_hot_residue_to_harden() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 800f, 100, solidified: true); // small (100 < 240) but not yet hardened

    Assert.Equal("smex:bessemer-status-coolingtochisel", SolidifiedStatus(be));
  }

  [Fact]
  public void Status_tells_the_player_to_chisel_a_small_hardened_residue() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 300f, 100, solidified: true); // small and hardened

    Assert.Equal("smex:bessemer-status-chiselout", SolidifiedStatus(be));
  }

  #endregion

  #region Chisel-out recovery

  [Fact]
  public void Chiselling_recovers_the_metal_and_clears_the_charge() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 300f, 100, solidified: true);

    ItemStack? drop = be.ChiselOutContent();

    Assert.NotNull(drop);
    Assert.Equal("game:metalbit-iron", drop!.Collectible.Code.ToString());
    Assert.Equal(20, drop.StackSize); // 5 units per bit
    Assert.Null(ReflectionHelpers.GetField(be, "_content"));
    Assert.Equal(0, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
    Assert.False((bool)ReflectionHelpers.GetField(be, "_solidified")!);
  }

  [Fact]
  public void Chiselling_a_non_chiselable_charge_returns_nothing_and_keeps_it() {
    var world = NewWorld();
    var be = Control(world);
    PrimeCharge(be, world, 800f, 100, solidified: true); // too hot to chisel

    Assert.Null(be.ChiselOutContent());
    Assert.Equal(100, (int)ReflectionHelpers.GetField(be, "_contentUnits")!);
  }

  #endregion

  /// <summary>Places a molten-canal cell at the control's resolved input-tap offset.</summary>
  private static BlockEntityMoltenCanal PlaceInputCell(
    TestWorld world,
    BlockEntityConverterControl control
  ) {
    var pos = (BlockPos)
      ReflectionHelpers.Invoke(
        control,
        "GetGlobalPos",
        InputTapLocal.x,
        InputTapLocal.y,
        InputTapLocal.z
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
}

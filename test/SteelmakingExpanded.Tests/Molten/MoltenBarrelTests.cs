using ExpandedLib.Testing;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The molten barrel stores a single liquid metal as an <see cref="ILiquidMetalSink"/>: it accepts
/// matching metal up to capacity, rejects a foreign metal, tracks temperature/hardening for its glow
/// and drops, and round-trips its contents. (The molded full-and-hardened drop path and the
/// player-coupled chisel-out are exercised in the integration suite.)
/// </summary>
public class MoltenBarrelTests {
  private const string Iron = "game:ingot-iron";

  private static TestWorld NewWorld() {
    var world = new TestWorld();
    world.RegisterItem(Iron, 1500f);
    world.RegisterItem("game:ingot-copper", 1084f);
    world.RegisterItem("game:metalbit-iron");
    return world;
  }

  private static BlockEntityMoltenBarrel Barrel(TestWorld world) {
    var be = new BlockEntityMoltenBarrel {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(new Block(), "smex:moltenbarrel", 50),
    };
    world.Attach(be);
    return be;
  }

  private static ItemStack Metal(TestWorld world, string code, float temp) =>
    MoltenMetal.CreateStack(world.World, code, temp)!;

  #region Receiving metal

  [Fact]
  public void An_empty_barrel_can_receive_and_is_not_full() {
    var world = NewWorld();
    var be = Barrel(world);
    Assert.True(be.CanReceiveAny);
    Assert.False(be.IsFull);
  }

  [Fact]
  public void ReceiveLiquidMetal_stores_metal_and_consumes_the_amount() {
    var world = NewWorld();
    var be = Barrel(world);

    int amount = 30;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1300f), ref amount, 1300f);

    Assert.Equal(0, amount);
    Assert.Equal(30, be.CurrentUnitAmount);
    Assert.NotNull(be.MetalContent);
    Assert.Equal(1300f, be.Temperature, 0);
  }

  [Fact]
  public void ReceiveLiquidMetal_clamps_to_capacity_and_returns_overflow() {
    var world = NewWorld();
    var be = Barrel(world);

    int amount = be.MaxUnitAmount + 25;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1300f), ref amount, 1300f);

    Assert.Equal(be.MaxUnitAmount, be.CurrentUnitAmount);
    Assert.Equal(25, amount); // overflow stays with the caller
    Assert.True(be.IsFull);
    Assert.False(be.CanReceiveAny);
  }

  [Fact]
  public void ReceiveLiquidMetal_rejects_a_foreign_metal() {
    var world = NewWorld();
    var be = Barrel(world);
    int first = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1300f), ref first, 1300f);

    int amount = 20;
    be.ReceiveLiquidMetal(
      Metal(world, "game:ingot-copper", 1000f),
      ref amount,
      1000f
    );

    Assert.Equal(20, amount); // nothing taken
    Assert.Equal(20, be.CurrentUnitAmount);
  }

  #endregion

  #region Thermal state

  [Theory]
  [InlineData(1400f, false)] // liquid
  [InlineData(300f, true)] // below the hardened threshold
  public void IsHardened_tracks_the_stored_temperature(
    float temp,
    bool hardened
  ) {
    var world = NewWorld();
    var be = Barrel(world);
    int amount = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, temp), ref amount, temp);

    Assert.Equal(hardened, be.IsHardened);
  }

  [Theory]
  [InlineData(400f, 0)] // below glow floor
  [InlineData(800f, 10)] // (800-500)/30
  public void GlowLightLevel_follows_the_metal_temperature(float temp, int glow) {
    var world = NewWorld();
    var be = Barrel(world);
    int amount = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, temp), ref amount, temp);

    Assert.Equal((byte)glow, be.GlowLightLevel);
  }

  [Fact]
  public void An_empty_barrel_has_no_glow_and_zero_temperature() {
    var world = NewWorld();
    var be = Barrel(world);
    Assert.Equal(0, be.GlowLightLevel);
    Assert.Equal(0f, be.Temperature);
  }

  #endregion

  #region Cooldown coefficient (live)

  // The VS time-based cooldown speed lives on the stack's temperature tree.
  private static float CooldownSpeedOf(ItemStack stack) =>
    (stack.Attributes["temperature"] as ITreeAttribute)?.GetFloat(
      "cooldownSpeed"
    ) ?? 0f;

  [Fact]
  public void The_barrel_cooldown_coefficient_defaults_to_one() {
    // Ships as a no-op multiplier (1x the molten-system rate); admins can slow the barrel down.
    Assert.Equal(1f, SmexValues.BarrelCooldownCoefficient, 3);
  }

  [Fact]
  public void Stored_metal_carries_the_barrel_cooldown_coefficient() {
    var world = NewWorld();
    var be = Barrel(world);
    int amount = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1300f), ref amount, 1300f);

    Assert.Equal(
      SmexValues.MoltenCooldownSpeed * SmexValues.BarrelCooldownCoefficient,
      CooldownSpeedOf(be.MetalContent!),
      3
    );
  }

  // Regression: a live `/exmod config smex barrelcooldowncoefficient ...` change must reach metal
  // already standing in the barrel, not just the next pour - the server tick re-stamps the live rate.
  [Fact]
  public void Changing_the_barrel_coefficient_reaches_metal_already_in_the_barrel() {
    var world = NewWorld();
    var be = Barrel(world);
    int amount = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1300f), ref amount, 1300f);

    float original = SmexValues.BarrelCooldownCoefficient;
    try {
      SmexValues.Edit(c => c.BarrelCooldownCoefficient = 5f);
      ReflectionHelpers.Invoke(be, "OnServerTick");

      Assert.Equal(
        SmexValues.MoltenCooldownSpeed * 5f,
        CooldownSpeedOf(be.MetalContent!),
        3
      );
    } finally {
      SmexValues.Edit(c => c.BarrelCooldownCoefficient = original);
    }
  }

  #endregion

  #region Drops

  [Fact]
  public void GetMetalDrops_yields_metalbits_for_a_partly_filled_barrel() {
    var world = NewWorld();
    var be = Barrel(world);
    int amount = 20;
    be.ReceiveLiquidMetal(Metal(world, Iron, 1300f), ref amount, 1300f);

    var drops = be.GetMetalDrops();

    Assert.Single(drops);
    Assert.Equal("game:metalbit-iron", drops[0].Collectible.Code.ToString());
    Assert.Equal(20 / 5, drops[0].StackSize); // 1 bit per 5 units
  }

  [Fact]
  public void GetMetalDrops_is_empty_for_an_empty_barrel() {
    var world = NewWorld();
    Assert.Empty(Barrel(world).GetMetalDrops());
  }

  #endregion

  #region Serialization

  [Fact]
  public void Contents_round_trip_through_the_tree() {
    var world = NewWorld();
    var src = Barrel(world);
    int amount = 35;
    src.ReceiveLiquidMetal(Metal(world, Iron, 1250f), ref amount, 1250f);

    var tree = new TreeAttribute();
    src.ToTreeAttributes(tree);

    var restored = Barrel(world);
    restored.FromTreeAttributes(tree, world.World);

    Assert.Equal(35, restored.CurrentUnitAmount);
    Assert.NotNull(restored.MetalContent);
    Assert.Equal(Iron, restored.MetalContent!.Collectible.Code.ToString());
  }

  #endregion
}

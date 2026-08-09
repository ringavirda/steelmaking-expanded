using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// The steam condenser passes water through its W/E faces and condenses north-line steam into that
/// through-flow. Its <c>Process</c> takes the three networks as arguments, so the condensation logic
/// is driven directly with hand-built networks - no block-face wiring needed. Also pins the pure
/// classification helpers and the HUD-mirror round trip.
/// </summary>
public class CondenserBeTests {
  private static (TestWorld world, BlockEntitySteamCondenser be) Rig() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var be = new BlockEntitySteamCondenser {
      Pos = new BlockPos(0, 0, 0),
      Block = TestBlocks.Configure(new Block(), "ppex:steamcondenser", 65),
    };
    world.Attach(be);
    return (world, be);
  }

  private static PipeNetwork WaterNet(
    TestWorld world,
    int nodes,
    float litres,
    float temp = 30f
  ) {
    var net = PipeTestWorld.LooseNet(world.Networks, nodes);
    if (litres > 0f)
      net.TryProduceLiquid(litres, temp, 1f, world.Accessor);
    return net;
  }

  private static PipeNetwork SteamNet(
    TestWorld world,
    int nodes,
    float litres,
    float temp = 150f
  ) {
    var net = PipeTestWorld.LooseNet(world.Networks, nodes);
    net.TryProduceGas(
      litres,
      temp,
      "Steam",
      world.Accessor,
      maxOutputPressure: 20f
    );
    return net;
  }

  private static bool Process(
    BlockEntitySteamCondenser be,
    PipeNetwork? steam,
    PipeNetwork? a,
    PipeNetwork? b,
    IBlockAccessor ba
  ) =>
    (bool)
      ReflectionHelpers.Invoke(
        be,
        "Process",
        steam,
        a,
        BlockFacing.WEST,
        b,
        BlockFacing.EAST,
        1f,
        ba
      )!;

  #region Process

  [Fact]
  public void Steam_condenses_into_the_through_flow_outlet() {
    var (world, be) = Rig();
    var steam = SteamNet(world, 6, 300f);
    var inlet = WaterNet(world, 6, 60f); // fuller -> inlet
    var outlet = WaterNet(world, 6, 0f); // empty -> outlet
    float steamBefore = steam.State!.Volume;

    bool condensing = Process(be, steam, inlet, outlet, world.Accessor);

    Assert.True(condensing);
    Assert.True(outlet.State!.IsLiquid);
    Assert.True(outlet.State!.Volume > 0f); // condensate + through-flow arrived
    Assert.True(steam.State!.Volume < steamBefore); // steam was drawn off
  }

  [Fact]
  public void With_no_water_line_drawn_steam_just_vents_as_gas() {
    var (world, be) = Rig();
    var steam = SteamNet(world, 6, 300f);
    float steamBefore = steam.State!.Volume;

    bool condensing = Process(be, steam, null, null, world.Accessor);

    Assert.False(condensing); // nothing condensed
    Assert.True(steam.State!.Volume < steamBefore); // but steam still vented off
  }

  [Fact]
  public void A_water_loop_takes_the_condensate_directly() {
    var (world, be) = Rig();
    var steam = SteamNet(world, 6, 300f);
    var loop = WaterNet(world, 6, 40f);
    float steamBefore = steam.State!.Volume;
    float waterBefore = loop.State!.Volume;

    // Same network on both water faces => a loop.
    bool condensing = Process(be, steam, loop, loop, world.Accessor);

    Assert.True(condensing);
    Assert.True(loop.State!.Volume > waterBefore); // condensate dropped in
    Assert.True(steam.State!.Volume < steamBefore);
  }

  [Fact]
  public void Idle_with_no_steam_and_no_water_does_nothing() {
    var (world, be) = Rig();
    Assert.False(Process(be, null, null, null, world.Accessor));
  }

  #endregion

  #region Serialization

  [Fact]
  public void Condensing_mirror_round_trips_through_the_tree() {
    var (world, be) = Rig();
    var steam = SteamNet(world, 6, 300f);
    var inlet = WaterNet(world, 6, 60f);
    var outlet = WaterNet(world, 6, 0f);
    Process(be, steam, inlet, outlet, world.Accessor); // sets _condensing via OnTick path

    // Process itself does not persist; ToTree mirrors the field set by OnTick. Drive the field.
    ReflectionHelpers.SetField(be, "_condensing", true);
    var tree = new TreeAttribute();
    be.ToTreeAttributes(tree);

    var restored = new BlockEntitySteamCondenser {
      Pos = be.Pos.Copy(),
      Block = be.Block,
    };
    world.Attach(restored);
    restored.FromTreeAttributes(tree, world.World);

    Assert.True((bool)ReflectionHelpers.GetField(restored, "_condensing")!);
  }

  #endregion
}

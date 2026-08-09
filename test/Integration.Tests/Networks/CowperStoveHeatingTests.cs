using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.Tests;
using SteelmakingExpanded;
using SteelmakingExpanded.BlockStructures.CowperStove.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// The cowper stove charges its brick core from hot furnace exhaust drawn off the gas network across
/// its connector face. This spans smex (the regenerator) and ppex (the <see cref="PipeNetwork"/>), so
/// it lives in the integration suite: a sealed exhaust run on the stove's south face heats the core
/// and is drained; an unfed stove sits cold.
/// </summary>
public class CowperStoveHeatingTests {
  private static BlockEntityCowperStove Stove(TestWorld world, BlockPos pos) {
    var be = new BlockEntityCowperStove {
      Pos = pos,
      Block = TestBlocks.Configure(
        new Block(),
        "smex:cowperstove-north",
        85,
        ("side", "north")
      ),
    };
    world.Attach(be);
    ReflectionHelpers.Invoke(be, "UpdateStructureRotation");
    // Prime the config-cached tunables Initialize would set (we don't run the full Initialize).
    ReflectionHelpers.SetField(
      be,
      "_intakeVolume",
      SmexValues.CowperIntakeVolume
    );
    ReflectionHelpers.SetField(
      be,
      "_factorDefault",
      SmexValues.CowperHeatingSpeedDefault
    );
    ReflectionHelpers.SetField(
      be,
      "_factorOtherCoal",
      SmexValues.CowperHeatingSpeedOtherCoal
    );
    ReflectionHelpers.SetField(
      be,
      "_factorAnthracite",
      SmexValues.CowperHeatingSpeedAnthracite
    );
    ReflectionHelpers.SetField(
      be,
      "_coolingSpeedExhaust",
      SmexValues.CowperCoolingSpeedExhaust
    );
    ReflectionHelpers.SetField(
      be,
      "_coolingSpeedAir",
      SmexValues.CowperCoolingSpeedAir
    );
    ReflectionHelpers.SetField(
      be,
      "_maxTemperature",
      SmexValues.CowperMaxTemperature
    );
    ReflectionHelpers.SetProperty(be, "StructureComplete", true);
    // Connector sits on the stove's local-south face; default is SOUTH, set explicitly for clarity.
    ReflectionHelpers.SetField(be, "_connectorFace", BlockFacing.SOUTH);
    return be;
  }

  /// <summary>A sealed 2-cell exhaust run butted against the stove's south face at <paramref name="pos"/>.</summary>
  private static PipeNetwork ExhaustRunSouthOf(TestWorld world, BlockPos pos) {
    var pipe = PipeTestWorld.MakePipe(orientation: "ns");
    var south1 = pos.AddCopy(BlockFacing.SOUTH); // adjacent, connector faces back north
    var south2 = south1.AddCopy(BlockFacing.SOUTH);
    world.Place(south1, pipe);
    world.Place(south2, pipe);

    var rock = TestBlocks.Configure(new Block(), "game:rock", 99);
    world.Place(south2.AddCopy(BlockFacing.SOUTH), rock); // cap the far end
    world.Place(pos, TestBlocks.Configure(new Block(), "game:rock", 98)); // stove cell caps near end

    world.AddNode(south1, "pipe");
    world.AddNode(south2, "pipe");
    return (PipeNetwork)world.NetworkAt(south1)!;
  }

  [Fact]
  public void Hot_exhaust_charges_the_core_and_is_drawn_off() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pos = new BlockPos(0, 4, 0);
    var stove = Stove(world, pos);
    var net = ExhaustRunSouthOf(world, pos);
    net.TryProduceGas(
      60f,
      900f,
      "Exhaust",
      world.Accessor,
      maxOutputPressure: 10f
    );
    float before = net.State!.Volume;

    ReflectionHelpers.Invoke(stove, "OnProductionTick", 1f);

    float core = (float)
      ReflectionHelpers.GetField(stove, "_internalTemperature")!;
    Assert.True(core > 20f, $"core should have heated, was {core}");
    Assert.True(
      net.State!.Volume < before,
      "exhaust should have been consumed"
    );
  }

  [Fact]
  public void An_unfed_stove_stays_cold() {
    var world = new TestWorld();
    world.RegisterNetwork("pipe", sys => new PipeNetwork(sys));
    var pos = new BlockPos(0, 4, 0);
    var stove = Stove(world, pos);
    // No exhaust run plumbed in.

    ReflectionHelpers.Invoke(stove, "OnProductionTick", 1f);

    Assert.Equal(
      20f,
      (float)ReflectionHelpers.GetField(stove, "_internalTemperature")!,
      1
    );
  }
}

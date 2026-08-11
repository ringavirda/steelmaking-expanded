using ExpandedLib.Testing;
using NSubstitute;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Run-length behaviour of the molten canal: a canal is plumbing, so how far the metal gets and how
/// fast it arrives must not depend on how many blocks it crossed on the way. These drive a
/// start-anchored run of a given length with the head kept topped up, and read what reaches the far
/// end - the case a two-cell flow test cannot see, and the one players build.
/// </summary>
public class MoltenRunLengthTests {
  private const string Iron = "game:ingot-iron";
  private const int ShortRun = 4;
  private const int LongRun = 20;

  private static CastingLine Line(int length, bool endsInPedestal = false) {
    var scene = new Scene().Network("molten", s => new MoltenNetwork(s));
    var line = new CastingLine(
      scene,
      new BlockPos(0, 0, 0),
      length,
      endsInPedestal
    );
    scene.Build();
    return line;
  }

  #region Reach

  [Fact]
  public void Metal_reaches_the_far_end_of_a_long_run() {
    var line = Line(LongRun);

    // A tap keeps the head charged; one tick per block is all a run should need to fill.
    line.Run(LongRun + 10, feedPerTick: 50);

    Assert.True(
      line.Last.CellAmount > 0,
      $"nothing reached block {LongRun - 1} of the run in {LongRun + 10} ticks"
    );
  }

  [Fact]
  public void A_fed_run_fills_every_one_of_its_cells() {
    var line = Line(LongRun);

    line.Run(LongRun * 3, feedPerTick: 50);

    for (int i = 1; i < LongRun; i++)
      Assert.True(
        line.Cell(i).CellAmount > 0,
        $"block {i} of a continuously fed run is still dry"
      );
  }

  #endregion

  #region Throughput

  [Fact]
  public void Delivery_does_not_slow_down_with_run_length() {
    int shortRunDelivery = DeliveredWhileRunning(ShortRun);
    int longRunDelivery = DeliveredWhileRunning(LongRun);

    // Both runs end in the same tap draining at the same speed, measured over the same window once
    // both are primed: the number of blocks in between must not throttle what comes out.
    Assert.True(
      longRunDelivery >= shortRunDelivery * 0.9,
      $"a {LongRun}-block run delivered {longRunDelivery} units where a {ShortRun}-block one "
        + $"delivered {shortRunDelivery}: throughput is falling off with length"
    );
  }

  /// <summary>Units a run of <paramref name="length"/> blocks pours into its barrel over a fixed
  /// window, measured after the run has had time to prime so only the steady rate is compared.</summary>
  private static int DeliveredWhileRunning(int length) {
    var line = Line(length).ParkBarrel(drainSpeed: 8f);

    line.Run(length * 3, feedPerTick: 50); // prime: fill the run
    int before = line.Tap!.BarrelCurrentUnits;
    line.Run(20, feedPerTick: 50); // measure the steady rate

    return line.Tap.BarrelCurrentUnits - before;
  }

  #endregion

  #region Branches

  [Fact]
  public void A_junction_feeds_both_of_its_branches() {
    // A tee two blocks out, one branch carrying on south and the other running east. Conveying hands
    // a whole surplus to the first branch the tee is asked about, so the second one is only served
    // once the first backs up - it still has to be served.
    var scene = new Scene().Network("molten", s => new MoltenNetwork(s));
    scene.World.RegisterItem(Iron, 1500f);

    var start = new BlockEntityMoltenCanalStart {
      Pos = new BlockPos(0, 0, 0),
      Block = CanalBlock(scene, 1, "straight", "ns"),
    };
    scene.Node(start.Pos, start.Block, start, "molten");
    Straight(scene, new BlockPos(0, 0, 1), 2, "ns");
    var tee = Cell(scene, new BlockPos(0, 0, 2), 3, "tjunction", "nes");
    var south = Straight(scene, new BlockPos(0, 0, 3), 4, "ns");
    var east = Straight(scene, new BlockPos(1, 0, 2), 5, "we");
    scene.Build();

    for (int i = 0; i < 12; i++) {
      start.PushMetal(
        50,
        MoltenMetal.CreateStack(scene.World.World, Iron, 1700f)!,
        scene.World.World
      );
      scene.Step(1);
    }

    Assert.True(tee.CellAmount > 0, "the junction itself never filled");
    Assert.True(south.CellAmount > 0, "the branch carrying on south stayed dry");
    Assert.True(east.CellAmount > 0, "the branch running east stayed dry");
  }

  private static BlockEntityMoltenCanal Straight(
    Scene scene,
    BlockPos pos,
    int id,
    string orientation
  ) => Cell(scene, pos, id, "straight", orientation);

  private static BlockEntityMoltenCanal Cell(
    Scene scene,
    BlockPos pos,
    int id,
    string type,
    string orientation
  ) {
    var block = CanalBlock(scene, id, type, orientation);
    var be = new BlockEntityMoltenCanal { Pos = pos.Copy(), Block = block };
    scene.Node(pos, block, be, "molten");
    return be;
  }

  /// <summary>A configured canal block: connectors come straight off the orientation letters, so
  /// reflection-setting Type/Orientation (OnLoaded is skipped headlessly) is enough.</summary>
  private static BlockMoltenCanal CanalBlock(
    Scene scene,
    int id,
    string type,
    string orientation
  ) {
    var item = new Item { Code = new AssetLocation(Iron) };
    scene.World.World.GetItem(Arg.Any<AssetLocation>()).Returns(item);

    var block = TestBlocks.Configure(
      new BlockMoltenCanal(),
      $"smex:moltencanal-{type}-{orientation}",
      id,
      ("type", type),
      ("orientation", orientation)
    );
    ReflectionHelpers.SetProperty(block, "Type", type);
    ReflectionHelpers.SetProperty(block, "Orientation", orientation);
    return block;
  }

  #endregion

  #region Dregs

  [Fact]
  public void The_last_unit_leaves_the_start_instead_of_stranding() {
    var line = Line(ShortRun);

    line.PourIn(1);
    line.Run(4);

    Assert.Equal(0, line.Head.CellAmount);
  }

  #endregion

  #region Saved configs

  [Fact]
  public void A_run_carries_metal_under_a_saved_pre_rebalance_config() {
    // The values every world created before the flow rebalance still carries: an upgrade only pushes
    // new defaults to keys a config migration names, so the shipped code has to work under these too.
    int flow = SmexValues.MoltenFlowRate;
    int minFlow = SmexValues.MoltenMinFlowAmount;
    int capacity = SmexValues.CanalDefaultUnitCapacity;
    try {
      SmexValues.Edit(c => {
        c.MoltenFlowRate = 50;
        c.MoltenMinFlowAmount = 10;
        c.CanalDefaultUnitCapacity = 50;
      });

      var line = Line(8);
      line.Run(40, feedPerTick: 50);

      Assert.True(
        line.Last.CellAmount > 0,
        "an old saved config stalls the run before its far end"
      );
    } finally {
      SmexValues.Edit(c => {
        c.MoltenFlowRate = flow;
        c.MoltenMinFlowAmount = minFlow;
        c.CanalDefaultUnitCapacity = capacity;
      });
    }
  }

  #endregion
}

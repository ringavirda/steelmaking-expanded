using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Models the casting line (handbook casting article): a furnace tap pours molten iron into a molten
/// canal run that carries it - cell to cell, flow simulated - to a fitting at the far end, which casts
/// it. Here the head cell stands in for the furnace tap (the metal is pushed straight in); the run
/// ends in a mold pedestal casting into a tool mold and/or a canal tap draining into a parked barrel.
/// The molten network flows the metal; the fittings drain it each server tick.
/// </summary>
internal sealed class CastingLine {
  private const string Iron = "game:ingot-iron";

  public readonly BlockEntityMoltenCanalStart Head;
  public readonly BlockEntityMoltenCanalMoldPedestal? Pedestal;
  public readonly BlockEntityMoltenCanalTap? Tap;

  private readonly Scene _scene;
  private readonly BlockEntityMoltenCanal[] _cells;

  /// <summary>
  /// Builds an N-cell ns canal run at <paramref name="origin"/>. The last cell is a mold pedestal
  /// (when <paramref name="endsInPedestal"/>) or a canal tap draining a barrel; the rest are straights.
  /// The head (origin) cell is a canal start - the only cell a furnace tap or a crucible can pour
  /// into, and the anchor the run's flow direction is measured from.
  /// </summary>
  public CastingLine(
    Scene scene,
    BlockPos origin,
    int length,
    bool endsInPedestal
  ) {
    _scene = scene;
    scene.World.RegisterItem(Iron, 1500f);

    _cells = new BlockEntityMoltenCanal[length];

    var headBlock = CanalBlock(scene, 1);
    Head = new BlockEntityMoltenCanalStart {
      Pos = origin.Copy(),
      Block = headBlock,
    };
    scene.Node(origin, headBlock, Head, "molten");
    _cells[0] = Head;

    for (int i = 1; i < length; i++) {
      BlockPos pos = origin.AddCopy(0, 0, i);
      bool last = i == length - 1;
      var block = CanalBlock(scene, i + 1);

      if (last && endsInPedestal) {
        Pedestal = new BlockEntityMoltenCanalMoldPedestal {
          Pos = pos.Copy(),
          Block = block,
        };
        scene.Node(pos, block, Pedestal, "molten");
        _cells[i] = Pedestal;
      } else if (last) {
        Tap = new BlockEntityMoltenCanalTap { Pos = pos.Copy(), Block = block };
        scene.Node(pos, block, Tap, "molten");
        Tap.TryTogglePouring(); // default is closed (severs); open it so the run reaches it
        _cells[i] = Tap;
      } else {
        _cells[i] = new BlockEntityMoltenCanal {
          Pos = pos.Copy(),
          Block = block,
        };
        scene.Node(pos, block, _cells[i], "molten");
      }
    }
  }

  private static BlockMoltenCanal CanalBlock(Scene scene, int id) {
    var item = new Item { Code = new AssetLocation("game:ingot-iron") };
    scene.World.World.GetItem(Arg.Any<AssetLocation>()).Returns(item);

    var block = TestBlocks.Configure(
      new BlockMoltenCanal(),
      "smex:moltencanal-straight-ns",
      id,
      ("type", "straight"),
      ("orientation", "ns")
    );
    ReflectionHelpers.SetProperty(block, "Type", "straight");
    ReflectionHelpers.SetProperty(block, "Orientation", "ns");
    return block;
  }

  /// <summary>Pours <paramref name="units"/> of hot molten iron into the head cell (the furnace tap).</summary>
  public CastingLine PourIn(int units, float temp = 1700f) {
    var metal = MoltenMetal.CreateStack(_scene.World.World, Iron, temp)!;
    Head.PushMetal(units, metal, _scene.World.World);
    return this;
  }

  /// <summary>Casts a parked barrel onto the tap at the given drain speed (units/tick).</summary>
  public CastingLine ParkBarrel(float drainSpeed) {
    Tap!.IsBarrel = true;
    // The tap reads its drain speed live from the block's "drainSpeed" attribute, so prime it there.
    Tap.Block.Attributes = new JsonObject(
      new JObject { ["drainSpeed"] = drainSpeed }
    );
    return this;
  }

  /// <summary>Sets an empty tool mold on the pedestal.</summary>
  public CastingLine SetMold() {
    Pedestal!.IsMold = true;
    return this;
  }

  /// <summary>
  /// Advances the line: each tick the furnace tap tops the head up by <paramref name="feedPerTick"/>
  /// units, the molten network flows metal toward the fitting, then the fitting drains its cell into
  /// the mold/barrel. Fittings are attached (not Initialized) so their drain tick is driven here
  /// rather than by the scene's listener pump - keeping the order explicit.
  /// </summary>
  public CastingLine Run(int ticks, int feedPerTick = 0) {
    for (int i = 0; i < ticks; i++) {
      if (feedPerTick > 0)
        PourIn(feedPerTick);
      _scene.Step(1); // molten network flow + cooling
      if (Pedestal != null)
        ReflectionHelpers.Invoke(Pedestal, "OnServerTick", 1f);
      if (Tap != null)
        ReflectionHelpers.Invoke(Tap, "OnServerTick", 1f);
    }
    return this;
  }

  /// <summary>The cell <paramref name="index"/> blocks along the run; 0 is the start.</summary>
  public BlockEntityMoltenCanal Cell(int index) => _cells[index];

  /// <summary>The far end of the run - the pedestal, the tap, or the last straight.</summary>
  public BlockEntityMoltenCanal Last => _cells[^1];

  /// <summary>Total liquid metal still standing in the canal cells (excludes what's cast into the mold/barrel).</summary>
  public int TotalInRun {
    get {
      int t = 0;
      foreach (var c in _cells)
        t += c.CellAmount;
      return t;
    }
  }
}

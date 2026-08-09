using ExpandedLib.Blocks.Construction;
using ExpandedLib.Testing;
using PipesAndPowerExpanded.BlockNetworkPipe;
using PipesAndPowerExpanded.BlockStructures.Boiler;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using PipesAndPowerExpanded.BlockStructures.Boiler.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using BoilerState = PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntityBoiler.BoilerState;

namespace PipesAndPowerExpanded.Tests;

/// <summary>
/// Assembles a fully-constructed, fired Cornish boiler that its production tick will actually run -
/// the wiring the simpler unit tests skip. It stands up the three game-coupled dependencies the tick
/// reads, using real types with their state forced through reflection:
/// <list type="bullet">
/// <item>a real <see cref="BlockBoilerCornish"/> (its geometry offsets fall back to hardcoded cells
/// when no JSON attributes are present, so no asset load is needed);</item>
/// <item>a complete <see cref="ExRightClickConstructable"/> (a one-stage construction reads as
/// finished), so <c>IsConstructed</c> is true;</item>
/// <item>a burning <see cref="BlockEntityCoalPile"/> in the firebox cell, so the fire is lit.</item>
/// </list>
/// Drive it with <see cref="Tick"/> and prime operating state with the <c>Set*</c> helpers.
/// </summary>
internal sealed class BoilerRig {
  public readonly TestWorld World;
  public readonly BlockEntityBoilerCornish Be;
  public readonly BlockBoilerCornish Block;
  public readonly BlockEntityCoalPile Pile;

  public BoilerRig() {
    World = new TestWorld();
    World.RegisterNetwork("pipe", sys => new PipeNetwork(sys));

    Block = TestBlocks.Configure(
      new BlockBoilerCornish(),
      "ppex:boiler-cornish-north",
      1,
      ("side", "north")
    );
    Be = new BlockEntityBoilerCornish {
      Pos = new BlockPos(0, 8, 0),
      Block = Block,
    };
    World.Place(Be.Pos, Block, Be);
    World.Attach(Be);
    BoilerFakes.ForceConstructed(Be);

    // A burning coal pile in the firebox cell (the geometry's fuel offset, rotated by the block angle).
    var fuelPos = Block.FuelWorldPos(Be.Pos);
    Pile = BoilerFakes.BurningPile(fuelPos);
    World.Place(
      fuelPos,
      TestBlocks.Configure(new Block(), "game:coalpile", 2),
      Pile
    );
  }

  /// <summary>Runs the production tick directly (bypassing the tick-listener scheduling).</summary>
  public void Tick(float dt = 1f, int times = 1) {
    for (int i = 0; i < times; i++)
      ReflectionHelpers.Invoke(Be, "OnProductionTick", dt);
  }

  public BoilerState State =>
    (BoilerState)ReflectionHelpers.GetField(Be, "_state")!;

  public float WaterVolume =>
    (float)ReflectionHelpers.GetField(Be, "_waterVolume")!;

  public float SteamVolume =>
    (float)ReflectionHelpers.GetField(Be, "_steamVolume")!;

  public BoilerRig SetState(BoilerState state) {
    ReflectionHelpers.SetField(Be, "_state", state);
    return this;
  }

  public BoilerRig SetWater(float litres) {
    ReflectionHelpers.SetField(Be, "_waterVolume", litres);
    return this;
  }

  public BoilerRig SetSteam(float litres) {
    ReflectionHelpers.SetField(Be, "_steamVolume", litres);
    return this;
  }

  public BoilerRig SetHeatingSeconds(float seconds) {
    ReflectionHelpers.SetField(Be, "_heatingSeconds", seconds);
    return this;
  }

  public BoilerRig SetShutdownSeconds(float seconds) {
    ReflectionHelpers.SetField(Be, "_shutdownSeconds", seconds);
    return this;
  }

  /// <summary>Snuffs the firebox so the next tick sees no fire.</summary>
  public BoilerRig ExtinguishFire() {
    ReflectionHelpers.SetField(Pile, "burning", false);
    return this;
  }

  /// <summary>Re-lights the firebox (the inverse of <see cref="ExtinguishFire"/>) for a re-fire cycle.</summary>
  public BoilerRig RelightFire() {
    ReflectionHelpers.SetField(Pile, "burning", true);
    return this;
  }
}

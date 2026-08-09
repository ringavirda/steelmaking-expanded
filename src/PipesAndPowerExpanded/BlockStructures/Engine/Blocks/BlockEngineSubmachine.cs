using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// Shared base for the engine sub-machines (fluid pump, air blower, MP generator). Its only job is
/// placement orientation: at an existing engine's sub-machine cell it snaps to the matching facing
/// (<see cref="BlockEngine.SubmachineSide"/>) instead of the player's look; with no engine nearby
/// it places normally. The reverse (engine onto an existing sub-machine) is handled in
/// <see cref="BlockEngine.OnBlockPlaced"/>.
/// </summary>
public abstract class BlockEngineSubmachine : Block {
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    // Only override orientation at an engine's sub-machine slot; else use normal placement.
    if (
      BlockEngine.TryFindEngineFor(
        world.BlockAccessor,
        blockSel.Position,
        out _,
        out BlockEngine engineBlock
      )
    ) {
      string side = BlockEngine.SubmachineSide(engineBlock.Variant["side"]);
      Block? oriented = world.GetBlock(CodeWithVariant("side", side));
      if (
        oriented != null
        && CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)
      ) {
        oriented.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
        return true;
      }
    }

    return base.TryPlaceBlock(
      world,
      byPlayer,
      itemstack,
      blockSel,
      ref failureCode
    );
  }
}

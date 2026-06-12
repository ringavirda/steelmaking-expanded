using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// Shared base for the engine sub-machines (fluid pump, air blower, MP generator). Its only
/// job is placement orientation: a sub-machine placed at the sub-machine cell of an already
/// present engine snaps to the facing that lines up with that engine
/// (<see cref="BlockEngine.SubmachineSide"/>) instead of the player's look direction, so the
/// sub-machine under an engine can only ever sit in the one correct orientation. With no
/// engine nearby it places normally via the <c>HorizontalOrientable</c> behavior.
/// <para>
/// The reverse direction (placing the engine onto an existing sub-machine) is handled by the
/// engine re-orienting the sub-machine in <see cref="BlockEngine.OnBlockPlaced"/>.
/// </para>
/// </summary>
public abstract class BlockEngineSubmachine : Block
{
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    // Only override the orientation when this cell is an engine's sub-machine slot; otherwise
    // fall through to the normal look-direction placement.
    if (
      BlockEngine.TryFindEngineFor(
        world.BlockAccessor,
        blockSel.Position,
        out _,
        out BlockEngine engineBlock
      )
    )
    {
      string side = BlockEngine.SubmachineSide(engineBlock.Variant["side"]);
      Block? oriented = world.GetBlock(CodeWithVariant("side", side));
      if (
        oriented != null
        && CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)
      )
      {
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

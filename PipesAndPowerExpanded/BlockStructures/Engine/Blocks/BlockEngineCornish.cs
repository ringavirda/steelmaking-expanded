using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The Cornish engine mega-block (steel, high-pressure tier). Adds the steam control
/// rods: sneak + right-click cycles the steam-admission throttle. Repairs require steel
/// only. All other behavior lives in <see cref="BlockEngineBase"/>.
/// </summary>
[EntityRegister]
public class BlockEngineCornish : BlockEngineBase
{
  protected override RepairItem[] RepairItems =>
    [
      new(["metalplate-steel"], 4, "steel plate"),
      new(["rod-steel"], 2, "steel rod"),
    ];

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    // A broken engine is handled by the base (wrench repair) regardless of sneak.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityEngineCornish be
      && !be.IsBroken
      && byPlayer.Entity.Controls.Sneak
    )
    {
      if (world.Side == EnumAppSide.Server)
        be.CycleThrottle();
      return true;
    }

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }
}

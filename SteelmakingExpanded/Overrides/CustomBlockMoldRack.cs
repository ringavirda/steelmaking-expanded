using SteelmakingExpanded.Networks.Molten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Overrides;

/// <summary>
/// Drop-in replacement for the vanilla <see cref="BlockMoldRack"/> that spills a
/// molten mold the moment it is placed on the rack. The rack stores molds in an
/// internal inventory (no opened GUI), so the player-inventory scan can't see
/// it — this catches the racked mold right after the vanilla put logic runs.
/// </summary>
public class CustomBlockMoldRack : BlockMoldRack
{
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    bool result = base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (
      world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityMoldRack rack
      && rack.Inventory is { } inv
    )
    {
      var notify = byPlayer as IServerPlayer;
      bool spilled = false;
      foreach (var slot in inv)
        spilled |= MoltenMoldSpill.SpillIfMolten(
          slot,
          world,
          spilled ? null : notify
        );
      if (spilled)
        rack.MarkDirty(true);
    }

    return result;
  }
}

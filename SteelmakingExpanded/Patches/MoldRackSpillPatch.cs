using HarmonyLib;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Patches;

/// <summary>
/// Harmony patch on the vanilla mold rack that spills a molten mold the moment
/// it is placed on the rack. The rack stores molds in an internal inventory (no
/// opened GUI), so the player-inventory scan can't see it — this catches the
/// racked mold right after the vanilla put logic runs.
/// </summary>
[HarmonyPatch(
  typeof(BlockMoldRack),
  nameof(BlockMoldRack.OnBlockInteractStart)
)]
public static class MoldRackSpillPatch
{
  public static void Postfix(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.Side != EnumAppSide.Server
      || world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not BlockEntityMoldRack rack
      || rack.Inventory is not { } inv
    )
      return;

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
}

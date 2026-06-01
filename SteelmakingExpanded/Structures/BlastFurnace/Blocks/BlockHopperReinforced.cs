using System.Linq;
using SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Util;

namespace SteelmakingExpanded.Structures.BlastFurnace.Blocks;

/// <summary>
/// The reinforced hopper that feeds the blast furnace. Right-click opens its
/// iron/coke/flux inventory; Ctrl + right-click toggles blast-mix dropping on the
/// bell hopper below.
/// </summary>
public class BlockHopperReinforced : Block
{
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is BlockEntityHopperReinforced be
    )
    {
      be.OnInteract(byPlayer);
      return true;
    }

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    // Plain RMB always opens the hopper inventory.
    var openHelp = new WorldInteraction
    {
      ActionLangCode = "smex:blockhelp-hopper-open",
      MouseButton = EnumMouseButton.Right,
    };

    if (
      world.BlockAccessor.GetBlockEntity(selection.Position.DownCopy())
      is BlockEntityHopperBell
    )
    {
      var toggleHelp = new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-hopper-toggle",
        HotKeyCodes = ["ctrl"],
        MouseButton = EnumMouseButton.Right,
      };
      return baseHelp.Append(openHelp).Append(toggleHelp).ToArray();
    }

    return baseHelp.Append(openHelp).ToArray();
  }
}

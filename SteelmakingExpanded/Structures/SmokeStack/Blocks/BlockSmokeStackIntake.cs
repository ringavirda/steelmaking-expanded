using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using SteelmakingExpanded.Networks.Gas.Blocks;
using SteelmakingExpanded.Structures.SmokeStack.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Structures.SmokeStack.Blocks;

/// <summary>
/// Intake/anchor block of the smoke-stack multiblock. Vents surplus exhaust gas from
/// the network to the sky; Ctrl + right-click shows the structure build outline.
/// </summary>
public class BlockSmokeStackIntake : BlockGasPassthrough
{
  public override Dictionary<string, string[]> AllowedOrientations =>
    new() { { "intake", ["n", "s", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) => "n";

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    // If the player holds CTRL and right clicks, trigger the multiblock hologram
    if (
      byPlayer.WorldData.EntityControls.CtrlKey
      && world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntitySmokeStack be
    )
    {
      be.Interact(byPlayer);
      (byPlayer as IClientPlayer)?.TriggerFpAnimation(
        EnumHandInteract.HeldItemInteract
      );
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

    if (
      world.BlockAccessor.GetBlockEntity(selection.Position)
        is BlockEntitySmokeStack be
      && !be.StructureComplete
    )
    {
      return baseHelp
        .Append(
          new WorldInteraction
          {
            ActionLangCode = "smex:blockhelp-mulblock-struc-show",
            HotKeyCodes = ["ctrl"],
            MouseButton = EnumMouseButton.Right,
          }
        )
        .Append(
          new WorldInteraction
          {
            ActionLangCode = "smex:blockhelp-mulblock-struc-hide",
            HotKeyCodes = ["ctrl", "shift"],
            MouseButton = EnumMouseButton.Right,
          }
        )
        .ToArray();
    }

    return baseHelp;
  }
}

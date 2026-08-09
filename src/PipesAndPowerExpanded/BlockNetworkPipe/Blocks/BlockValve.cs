using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Manually-toggled in-line valve on a pipe run. Open, it is a normal pipe node and the run flows
/// through; closed, it severs the run at its cell (see
/// <see cref="BlockEntityValve.IsConnectionBroken"/>). Empty-hand right-click toggles it.
/// </summary>
[BlockRegister]
public partial class BlockValve : BlockPipe {
  // Cached once - consulted on every placement/neighbour recalc, so it must not allocate per read.
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "valve", ["ns", "we", "ud", "sn", "ew", "du"] } };

  protected override string GetFallbackOrientation(string? type) => "ns";

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is BlockEntityValve be
    ) {
      // Don't toggle while holding an item/block.
      if (!byPlayer.Entity.RightHandItemSlot.Empty)
        return false;

      // ToggleOpen re-walks the network so the connectivity change applies immediately.
      if (world.Side == EnumAppSide.Server)
        be.ToggleOpen();

      ExSounds.PlayAt(
        world,
        blockSel.Position,
        ExSounds.CokeOvenDoorOpen,
        byPlayer
      );

      return true;
    }
    return true;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var toggleHelp = new WorldInteraction {
      ActionLangCode = "ppex:blockhelp-valve-toggle",
      MouseButton = EnumMouseButton.Right,
    };

    return baseHelp.Append(toggleHelp).ToArray();
  }
}

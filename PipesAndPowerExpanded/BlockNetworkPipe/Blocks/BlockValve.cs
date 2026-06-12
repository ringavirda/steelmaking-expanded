using System.Collections.Generic;
using System.Linq;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Manually-toggled gas valve sitting in-line on a pipe run. While open it is a normal
/// pipe node and the run flows straight through it (so a machine port butted against it
/// reads the run directly); while closed it severs the run at its own cell (see
/// <see cref="BlockEntityValve.IsConnectionBroken"/>), splitting it in two. Right-clicking
/// with an empty hand toggles it.
/// </summary>
[EntityRegister]
public class BlockValve : BlockPipe
{
  // Cached once — this is consulted on every placement/neighbour recalculation, so it
  // must not allocate a fresh dictionary per read.
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "valve", ["ns", "we", "ud", "sn", "ew", "du"] } };

  protected override string GetFallbackOrientation(string? type) => "ns";

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is BlockEntityValve be
    )
    {
      // Prevent toggling if the player is holding an item/block
      if (!byPlayer.Entity.RightHandItemSlot.Empty)
        return false;

      // Toggling changes graph connectivity: opening rejoins the two sides into one run,
      // closing severs them. ToggleOpen re-walks the network to apply that immediately.
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
  )
  {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var toggleHelp = new WorldInteraction
    {
      ActionLangCode = "ppex:blockhelp-valve-toggle",
      MouseButton = EnumMouseButton.Right,
    };

    return baseHelp.Append(toggleHelp).ToArray();
  }
}

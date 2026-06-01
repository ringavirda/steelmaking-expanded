using System.Collections.Generic;
using System.Linq;
using BlockNetworkLib;
using SteelmakingExpanded.Networks.Gas.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Manually-toggled gas valve. Right-clicking with an empty hand opens or closes it;
/// while closed it severs the network at this position (see
/// <see cref="BlockEntities.BlockEntityGasValve.IsConnectionBroken"/>).
/// </summary>
public class BlockGasValve : BlockGasPipe
{
  public override Dictionary<string, string[]> AllowedOrientations =>
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
      is BlockEntityGasValve be
    )
    {
      // Prevent toggling if the player is holding an item/block
      if (!byPlayer.Entity.RightHandItemSlot.Empty)
        return false;

      // The valve no longer swaps to a separate "opened"/"closed" block — the
      // state lives on the block entity and is shown via the held "open"
      // animation pose. We just flip it and re-walk the network so the new
      // open/closed connectivity (see BlockEntityGasValve.IsConnectionBroken)
      // takes effect immediately.
      if (world.Side == EnumAppSide.Server)
      {
        be.ToggleOpen();
        var netManager =
          world.Api.ModLoader.GetModSystem<BlockNetworkModSystem>();
        netManager.RemoveNode(world.BlockAccessor, blockSel.Position);
        netManager.AddNode(world.BlockAccessor, blockSel.Position, "gas");
      }

      world.PlaySoundAt(
        SmexSounds.Build,
        blockSel.Position.X,
        blockSel.Position.Y,
        blockSel.Position.Z,
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
      ActionLangCode = "smex:blockhelp-valve-toggle",
      MouseButton = EnumMouseButton.Right,
    };

    return baseHelp.Append(toggleHelp).ToArray();
  }
}

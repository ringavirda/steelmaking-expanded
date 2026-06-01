using System.Collections.Generic;
using System.Linq;
using SteelmakingExpanded.Networks.Gas.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Networks.Gas.Blocks;

/// <summary>
/// Pressure valve: a network endpoint that vents its network once the gas volume
/// exceeds a player-cycled threshold (Ctrl + right-click to change the threshold).
/// </summary>
public class BlockGasPressureValve : BlockGasValve
{
  public override Dictionary<string, string[]> AllowedOrientations =>
    new() { { "pressurevalve", ["ns", "we", "ud", "sn", "ew", "du"] } };

  public override bool IsNetworkEndPoint => true;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (byPlayer.Entity.Controls.CtrlKey)
    {
      if (
        world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityGasPressureValve be
      )
      {
        if (world.Side == EnumAppSide.Server)
        {
          be.CycleThreshold();
        }
        return true;
      }
    }
    return false;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var baseHelp = base.GetPlacedBlockInteractionHelp(
        world,
        selection,
        forPlayer
      )
      .ToList();

    baseHelp.RemoveAll(x => x.ActionLangCode == "smex:blockhelp-valve-toggle");

    baseHelp.Add(
      new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-pressurevalve-cycle",
        HotKeyCodes = ["ctrl"],
        MouseButton = EnumMouseButton.Right,
        RequireFreeHand = false,
      }
    );

    return baseHelp.ToArray();
  }
}

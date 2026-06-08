using System.Collections.Generic;
using System.Linq;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Pressure valve: a network endpoint that automatically vents an adjacent network's
/// gas to atmosphere whenever its pressure exceeds the valve's own material rating
/// (copper 1.5 / bronze·brass 3 / iron 5 / steel 15 atm). No manual interaction.
/// </summary>
[EntityRegister]
public class BlockPressureValve : BlockValve
{
  public override Dictionary<string, string[]> AllowedOrientations =>
    new() { { "pressurevalve", ["ns", "we", "ud", "sn", "ew", "du"] } };

  public override bool IsNetworkEndPoint => true;

  // The valve is automatic; suppress the inherited open/close toggle.
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) => false;

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) =>
    base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
      .Where(x => x.ActionLangCode != "ppex:blockhelp-valve-toggle")
      .ToArray();
}

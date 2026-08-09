using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Pressure valve: a network endpoint that spills its input network's overflow (above the
/// player-set gate pressure) into the output network, or vents to atmosphere when none is plumbed.
/// Right-click raises the gate, sneak+right-click lowers it (see
/// <see cref="BlockEntityPressureValve.GatePressureStep"/>), bounded by the valve's material rating.
/// </summary>
[BlockRegister]
public partial class BlockPressureValve : BlockValve {
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "pressurevalve", ["ns", "we", "ud", "sn", "ew", "du"] } };

  public override bool IsNetworkEndPoint => true;

  // Right-click adjusts the gate pressure instead of the inherited open/close toggle:
  // plain RMB raises it, sneak + RMB lowers it.
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityPressureValve be
    )
      return false;

    // Don't hijack the right-click while the player is placing/using a held item.
    if (!byPlayer.Entity.RightHandItemSlot.Empty)
      return false;

    if (world.Side == EnumAppSide.Client)
      return true;

    bool increase = !byPlayer.Entity.Controls.ShiftKey;
    if (be.AdjustGatePressure(increase))
      ExSounds.PlayAt(
        world,
        blockSel.Position,
        ExSounds.ToggleSwitch,
        byPlayer
      );

    return true;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) =>
    base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
      .Where(x => x.ActionLangCode != "ppex:blockhelp-valve-toggle")
      .Append(
        new WorldInteraction {
          ActionLangCode = "ppex:blockhelp-pressurevalve-increase",
          MouseButton = EnumMouseButton.Right,
        }
      )
      .Append(
        new WorldInteraction {
          ActionLangCode = "ppex:blockhelp-pressurevalve-decrease",
          MouseButton = EnumMouseButton.Right,
          HotKeyCode = "sneak",
        }
      )
      .ToArray();
}

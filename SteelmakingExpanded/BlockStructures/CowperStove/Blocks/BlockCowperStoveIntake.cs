using System.Collections.Generic;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.Structures.Metalworking.CowperStove.Blocks;

/// <summary>
/// Intake/anchor block of the cowper-stove multiblock. Routes exhaust gas into the
/// stove. The build-outline projection (Ctrl + Shift + right-click) is provided by the
/// shared <c>MultiblockStructure</c> block behavior declared in the block JSON.
/// </summary>
[EntityRegister]
public class BlockCowperStoveIntake : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  /// <summary>The exhaust connector sits on the intake's local-south face, rotated with the block.</summary>
  public bool HasConnectorAt(BlockFacing face) =>
    face
    == ExOrientation.RotateFacing(
      BlockFacing.SOUTH,
      ExOrientation.AngleFromSide(Variant["side"])
    );

  /// <summary>Includes the refractory tier in the display name.</summary>
  public override string GetHeldItemName(ItemStack itemStack) =>
    ExBlockNames.Decorate(this, base.GetHeldItemName(itemStack));
}

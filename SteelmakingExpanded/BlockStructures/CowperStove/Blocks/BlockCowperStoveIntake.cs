using System.Collections.Generic;
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

  public bool HasConnectorAt(BlockFacing face)
  {
    var orient = Variant["side"];
    return orient != null
      && (
        (orient == "north" && face == BlockFacing.SOUTH)
        || (orient == "east" && face == BlockFacing.WEST)
        || (orient == "south" && face == BlockFacing.NORTH)
        || (orient == "west" && face == BlockFacing.EAST)
      );
  }
}

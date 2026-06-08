using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The air-blower sub-machine. Exposes a single pipe connector on its left face
/// (rotated to the placed orientation) through which it pushes pressurised air.
/// </summary>
[EntityRegister]
public class BlockEngineAirBlower : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  private BlockFacing LeftFace =>
    StructureFillers.RotateFacing(
      BlockFacing.WEST,
      StructureFillers.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) => face == LeftFace;
}

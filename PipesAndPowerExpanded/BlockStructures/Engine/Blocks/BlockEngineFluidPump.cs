using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The fluid-pump sub-machine. Exposes pipe connectors on its bottom (water source)
/// and left (delivery) faces, rotated to the placed orientation.
/// </summary>
[EntityRegister]
public class BlockEngineFluidPump : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  private BlockFacing LeftFace =>
    StructureFillers.RotateFacing(
      BlockFacing.WEST,
      StructureFillers.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) =>
    face == BlockFacing.DOWN || face == LeftFace;
}

using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The fluid-pump sub-machine. Exposes pipe connectors on its bottom (water source)
/// and left (delivery) faces, rotated to the placed orientation.
/// </summary>
[EntityRegister]
public class BlockEngineFluidPump : BlockEngineSubmachine, INetworkConnector
{
  public string NetworkType => "pipe";

  private BlockFacing LeftFace =>
    ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) =>
    face == BlockFacing.DOWN || face == LeftFace;
}

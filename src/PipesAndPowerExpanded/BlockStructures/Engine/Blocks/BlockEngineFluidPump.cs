using ExpandedLib.Blocks.Networks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The fluid-pump sub-machine. Exposes pipe connectors on its bottom (water source)
/// and left (delivery) faces, rotated to the placed orientation.
/// </summary>
[BlockRegister]
public partial class BlockEngineFluidPump
  : BlockEngineSubmachine,
    INetworkConnector {
  public string NetworkType => "pipe";

  private BlockFacing LeftFace =>
    ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) =>
    face == BlockFacing.DOWN || face == LeftFace;
}

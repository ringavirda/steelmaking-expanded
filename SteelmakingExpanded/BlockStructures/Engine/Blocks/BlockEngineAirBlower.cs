using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The air-blower sub-machine. Exposes a single pipe connector on its left face
/// (rotated to the placed orientation) through which it pushes pressurised air.
/// </summary>
[EntityRegister]
public class BlockEngineAirBlower : BlockEngineSubmachine, INetworkConnector
{
  public string NetworkType => "pipe";

  private BlockFacing LeftFace =>
    ExOrientation.RotateFacing(
      BlockFacing.WEST,
      ExOrientation.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) => face == LeftFace;
}

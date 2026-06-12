using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Passthrough pipe: carries gas straight through a wall. A connector butted against a
/// solid (non-air) block is not treated as a leak, so it seals against machine housings
/// without any cooperation from those blocks.
/// </summary>
[EntityRegister]
public class BlockPipePassthrough : BlockPipe
{
  /// <summary>Passthroughs never burst — they're embedded in walls/machine housings where a
  /// fracture would be unreachable, so they're exempt from over-pressure failure.</summary>
  public override float BurstPressure => float.MaxValue;

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new()
    {
      { "passthrough", ["ns", "we", "ud"] },
      {
        "passthroughbend",
        ["nw", "se", "en", "ws", "un", "us", "uw", "ue", "dn", "ds", "dw", "de"]
      },
    };

  protected override string GetFallbackOrientation(string? type) =>
    type == "passthroughbend" ? "nw" : "ns";

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => SideSolid[blockFace.Index] || HasConnectorAt(blockFace);

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  ) { }
}

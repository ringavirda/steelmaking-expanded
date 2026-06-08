using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockNetworkPipe.Blocks;

/// <summary>
/// Passthrough pipe: carries gas straight through a wall and seals against an
/// <see cref="IPipeSealingBlock"/> neighbour without registering it as a leak.
/// </summary>
[EntityRegister]
public class BlockPipePassthrough : BlockPipe
{
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

  public override bool IsValidNonNetworkConnection(
    Block neighborBlock,
    BlockFacing face
  ) => neighborBlock is IPipeSealingBlock;

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  ) { }
}

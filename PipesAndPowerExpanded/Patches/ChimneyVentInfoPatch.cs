using ExpandedLib.BlockNetworks;
using HarmonyLib;
using PipesAndPowerExpanded.BlockNetworkPipe.BlockEntities;
using PipesAndPowerExpanded.BlockNetworkPipe.Blocks;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.Patches;

/// <summary>
/// Harmony patch on the vanilla chimney block. When a chimney caps the open top
/// connector of one of our passthrough / passthrough-bend / outlet pipes, the network
/// draws gas through it (see <c>PipeNetwork</c>); this postfix adds a look-at info line
/// to the chimney so the player can see it is venting the network.
/// </summary>
[HarmonyPatch(typeof(Block), nameof(Block.GetPlacedBlockInfo))]
public static class ChimneyVentInfoPatch
{
  public static void Postfix(
    Block __instance,
    IWorldAccessor world,
    BlockPos pos,
    ref string __result
  )
  {
    // Only vanilla (or any) chimney blocks; everything else passes through untouched.
    if (__instance.Code?.Path?.Contains("chimney") != true)
      return;

    // The chimney only draws when sitting directly on a passthrough / passthrough-bend /
    // outlet whose top face is a connector.
    BlockPos below = pos.DownCopy();
    Block belowBlock = world.BlockAccessor.GetBlock(below);
    if (belowBlock is not (BlockPipePassthrough or BlockPipeOutlet))
      return;
    if (
      belowBlock is not BlockNetworkNode node
      || !node.HasConnectorAt(BlockFacing.UP)
    )
      return;

    bool hasGas =
      world.BlockAccessor.GetBlockEntity(below) is BlockEntityPipe pipe
      && pipe.Volume > 0f;

    __result +=
      Lang.Get(
        hasGas ? "ppex:chimney-info-venting" : "ppex:chimney-info-idle",
        PpexValues.ChimneyGasDrawRate
      ) + "\n";
  }
}

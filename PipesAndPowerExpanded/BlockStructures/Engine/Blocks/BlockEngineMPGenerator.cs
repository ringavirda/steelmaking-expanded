using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The MP-generator sub-machine. Couples axles on both ends of its axis (north +
/// south in the natural orientation), driving the vanilla mechanical-power network
/// via the <see cref="BEBehaviorEngineMPGenerator"/> torque producer.
/// </summary>
[EntityRegister]
public class BlockEngineMPGenerator
  : BlockEngineSubmachine,
    IMechanicalPowerBlock
{
  private bool IsXAxis => Variant["side"] is "east" or "west";

  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face,
    BlockMPBase forBlock
  ) =>
    IsXAxis
      ? face == BlockFacing.EAST || face == BlockFacing.WEST
      : face == BlockFacing.NORTH || face == BlockFacing.SOUTH;

  public void DidConnectAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
  ) { }

  public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
    world
      .BlockAccessor.GetBlockEntity(pos)
      ?.GetBehavior<BEBehaviorEngineMPGenerator>()
      ?.Network;
}

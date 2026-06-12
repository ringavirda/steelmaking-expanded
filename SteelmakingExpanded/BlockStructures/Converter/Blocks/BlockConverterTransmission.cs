using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent.Mechanics;

namespace SteelmakingExpanded.BlockStructures.Converter.Blocks;

/// <summary>
/// Mechanical-power intake for the converter. Couples an axle on the south face
/// in its natural (north) orientation; the connector follows the "side" variant.
/// </summary>
[EntityRegister]
public class BlockConverterTransmission : Block, IMechanicalPowerBlock
{
  private BlockFacing ConnectorFace =>
    Variant["side"] switch
    {
      "north" => BlockFacing.SOUTH,
      "east" => BlockFacing.WEST,
      "south" => BlockFacing.NORTH,
      "west" => BlockFacing.EAST,
      _ => BlockFacing.SOUTH,
    };

  /// <summary>Accepts an axle only on the connector face derived from the block's orientation.</summary>
  public bool HasMechPowerConnectorAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face,
    BlockMPBase forBlock
  ) => face == ConnectorFace;

  public void DidConnectAt(
    IWorldAccessor world,
    BlockPos pos,
    BlockFacing face
  ) { }

  /// <summary>Returns the mechanical-power network driving the converter, via the transmission's MP behavior.</summary>
  public MechanicalNetwork? GetNetwork(IWorldAccessor world, BlockPos pos) =>
    world
      .BlockAccessor.GetBlockEntity(pos)
      ?.GetBehavior<BEBehaviorMPConverterTransmission>()
      ?.Network;

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neighbour
  )
  {
    base.OnNeighbourBlockChange(world, pos, neighbour);
  }
}

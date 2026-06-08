using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.Blocks;

/// <summary>
/// The boiler's steam outlet: a fixed structure port (not a network node) that
/// exposes a single pipe connector on the face it is turned to face. A steam pipe
/// docks against that connector and the boiler pushes its steam into the network on
/// the other side of it. Horizontally orientable so it can be aimed at the pipe run.
/// </summary>
[EntityRegister]
public class BlockBoilerSteamOutlet : Block, INetworkConnector
{
  public string NetworkType => "pipe";

  /// <summary>
  /// The single horizontal face that carries the pipe connector, derived from the
  /// block's <c>side</c> variant (north → north, rotated for the other sides). The
  /// boiler reads the steam network in the cell on the other side of this face.
  /// </summary>
  public BlockFacing ConnectorFace =>
    StructureFillers.RotateFacing(
      BlockFacing.NORTH,
      StructureFillers.AngleFromSide(Variant["side"])
    );

  public bool HasConnectorAt(BlockFacing face) => face == ConnectorFace;

  public override bool CanAttachBlockAt(
    IBlockAccessor world,
    Block block,
    BlockPos pos,
    BlockFacing blockFace,
    Cuboidi attachmentArea
  ) => HasConnectorAt(blockFace) || SideSolid[blockFace.Index];

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  )
  {
    base.OnBlockPlaced(world, blockPos, byItemStack);
    if (world.Side == EnumAppSide.Server)
      TryLinkBoiler(world, blockPos);
  }

  /// <summary>
  /// Binds this outlet to an adjacent boiler when it is placed on the boiler's
  /// designated outlet cell, recording the link on both block entities.
  /// </summary>
  private static void TryLinkBoiler(IWorldAccessor world, BlockPos pos)
  {
    foreach (var face in BlockFacing.ALLFACES)
    {
      BlockPos npos = pos.AddCopy(face);

      // The outlet cell is surrounded by the boiler's filler cells, so the boiler
      // is reached either directly or through one of its fillers.
      BlockPos? boilerPos = world.BlockAccessor.GetBlockEntity(npos)
        is BlockEntityStructureFiller filler
        ? filler.Principal
        : npos;

      if (boilerPos == null)
        continue;
      if (
        world.BlockAccessor.GetBlock(boilerPos) is not BlockBoilerBase boiler
        || world.BlockAccessor.GetBlockEntity(boilerPos)
          is not BlockEntityBoilerBase boilerBe
      )
        continue;

      // Only bind when this really is the boiler's outlet slot.
      if (!boiler.OutletWorldPos(boilerPos).Equals(pos))
        continue;

      boilerBe.LinkOutlet(pos);
      if (
        world.BlockAccessor.GetBlockEntity(pos)
        is BlockEntityBoilerSteamOutlet outletBe
      )
      {
        outletBe.BoilerPos = boilerPos.Copy();
        outletBe.MarkDirty(true);
      }
      return;
    }
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    // Drop the boiler-side link before the standard break teardown.
    if (
      world.BlockAccessor.GetBlockEntity(pos)
        is BlockEntityBoilerSteamOutlet outletBe
      && outletBe.BoilerPos != null
      && world.BlockAccessor.GetBlockEntity(outletBe.BoilerPos)
        is BlockEntityBoilerBase boilerBe
    )
      boilerBe.LinkOutlet(null);

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }
}

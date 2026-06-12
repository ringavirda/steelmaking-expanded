using System;
using System.Collections.Generic;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>
/// The blast furnace door — the anchor block of the furnace multiblock. Handles
/// oriented placement of the structure and routes Ctrl + right-click to the
/// <see cref="BlockEntityBlastFurnace"/> for the show/hide structure outline.
/// </summary>
[EntityRegister]
public class BlockBlastFurnaceDoor : BlockBeeHiveKilnDoor
{
  public override bool TryPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    BlockPos position = blockSel.Position;
    IBlockAccessor blockAccessor = world.BlockAccessor;

    return blockAccessor.GetBlock(position, 1).Id == 0
      && CanPlaceBlock(world, byPlayer, blockSel, ref failureCode)
      && PlaceBFDoor(
        world,
        byPlayer,
        itemstack,
        blockSel,
        position,
        blockAccessor
      );
  }

  private bool PlaceBFDoor(
    IWorldAccessor world,
    IPlayer byPlayer,
    ItemStack itemstack,
    BlockSelection blockSel,
    BlockPos pos,
    IBlockAccessor ba
  )
  {
    ba.SetBlock(BlockId, pos);
    var beBlastFurnace = ba.GetBlockEntity<BlockEntityBlastFurnace>(pos);
    var beBehaviorDoor = beBlastFurnace.GetBehavior<BEBehaviorDoor>();

    beBehaviorDoor.RotateYRad = BEBehaviorDoor.getRotateYRad(
      byPlayer,
      blockSel
    );
    beBehaviorDoor.RotateYRad +=
      (beBehaviorDoor.RotateYRad == -MathF.PI) ? -MathF.PI : MathF.PI;

    // Triggers the immediate rotation math in the BE so holograms work on tick 1
    beBlastFurnace.Init();

    if (world.Side == EnumAppSide.Server)
    {
      GetBehavior<BlockBehaviorDoor>().placeMultiblockParts(world, pos);
      beBlastFurnace.MarkDirty(true);
    }

    return true;
  }

  /// <summary>
  /// Replicates the standard <see cref="Block.GetDrops"/> logic instead of calling
  /// <c>base</c>. The inherited <see cref="BlockBeeHiveKilnDoor.GetDrops"/> looks up a
  /// <c>BlockEntityBeeHiveKiln</c> to stamp <c>totalHoursHeatReceived</c> onto the drop;
  /// our block entity is a <see cref="BlockEntityBlastFurnace"/>, so that lookup returns
  /// null and the vanilla method NREs server-side, disconnecting the player who breaks
  /// the door. The blast furnace has no firing progress to preserve on the dropped item,
  /// so we simply return the normal drops.
  /// </summary>
  // CS8603: this faithfully mirrors Block.GetDrops, which returns null to mean
  // "no drops" — the caller (SpawnDropsAndRemoveBlock) null-guards the result.
#pragma warning disable CS8603
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    bool preventDefault = false;
    List<ItemStack> behaviorDrops = [];

    foreach (BlockBehavior behavior in BlockBehaviors)
    {
      EnumHandling handling = EnumHandling.PassThrough;
      ItemStack[]? drops = behavior.GetDrops(
        world,
        pos,
        byPlayer,
        ref dropQuantityMultiplier,
        ref handling
      );
      if (drops != null)
        behaviorDrops.AddRange(drops);

      if (handling == EnumHandling.PreventSubsequent)
        return drops;
      if (handling == EnumHandling.PreventDefault)
        preventDefault = true;
    }

    if (preventDefault)
      return behaviorDrops.ToArray();

    if (Drops == null)
      return null;

    List<ItemStack> result = [];
    foreach (BlockDropItemStack drop in Drops)
    {
      ItemStack? stack = drop.ToRandomItemstackForPlayer(
        byPlayer,
        world,
        dropQuantityMultiplier
      );
      if (stack != null)
      {
        result.Add(stack);
        if (drop.LastDrop)
          break;
      }
    }

    result.AddRange(behaviorDrops);
    return result.ToArray();
  }
#pragma warning restore CS8603
}

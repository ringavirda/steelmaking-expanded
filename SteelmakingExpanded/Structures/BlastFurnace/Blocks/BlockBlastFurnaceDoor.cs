using System;
using SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.Structures.BlastFurnace.Blocks;

/// <summary>
/// The blast furnace door — the anchor block of the furnace multiblock. Handles
/// oriented placement of the structure and routes Ctrl + right-click to the
/// <see cref="BlockEntityBlastFurnace"/> for the show/hide structure outline.
/// </summary>
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

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      byPlayer.WorldData.EntityControls.CtrlKey
      && world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityBlastFurnace beBlastFurnace
    )
    {
      beBlastFurnace.Interact(byPlayer);
      (byPlayer as IClientPlayer)?.TriggerFpAnimation(
        EnumHandInteract.HeldItemInteract
      );
      return true;
    }

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var blockEntity =
      world.BlockAccessor.GetBlockEntity<BlockEntityBlastFurnace>(
        selection.Position
      );

    if (blockEntity != null && !blockEntity.StructureComplete)
    {
      return baseHelp
        .Append(
          new WorldInteraction
          {
            ActionLangCode = "smex:blockhelp-mulblock-struc-show",
            HotKeyCodes = ["ctrl"],
            MouseButton = EnumMouseButton.Right,
          }
        )
        .Append(
          new WorldInteraction
          {
            ActionLangCode = "smex:blockhelp-mulblock-struc-hide",
            HotKeyCodes = ["ctrl", "shift"],
            MouseButton = EnumMouseButton.Right,
          }
        );
    }

    return baseHelp;
  }
}

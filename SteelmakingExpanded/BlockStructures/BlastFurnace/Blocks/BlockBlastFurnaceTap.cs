using System.Linq;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.Blocks;

/// <summary>
/// Molten metal tap on the blast furnace. Right-clicking with an empty hand toggles
/// pouring; the lower tap drains iron and the upper drains slag into a canal start
/// beneath the spout.
/// </summary>
[EntityRegister]
public class BlockBlastFurnaceTap : Block
{
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is BlockEntityBlastFurnaceTap tap
    )
    {
      // Prevent toggling if the player is holding an item/block
      if (!byPlayer.Entity.RightHandItemSlot.Empty)
        return false;

      // Opening requires a canal start directly below the tap's spout.
      bool isOpening = !tap.IsPouring;
      if (isOpening)
      {
        BlockFacing facing = BlockFacing.FromCode(Variant["side"]);
        BlockPos startPos = blockSel
          .Position.AddCopy(facing.Opposite)
          .DownCopy();
        if (world.BlockAccessor.GetBlock(startPos) is not BlockMoltenCanalStart)
        {
          (world.Api as ICoreClientAPI)?.TriggerIngameError(
            this,
            "nocanal",
            Lang.Get("smex:tap-err-nocanal")
          );
          return true;
        }
      }

      // The tap no longer swaps to a separate opened/closed block — its pouring
      // state lives on the block entity and is shown by holding the "open"
      // animation pose (see BlockEntityBlastFurnaceTap.ApplyPourPose).
      if (world.Side == EnumAppSide.Server)
        tap.TogglePouring();

      world.PlaySoundAt(
        SmexSounds.CokeOvenDoorOpen,
        blockSel.Position.X,
        blockSel.Position.Y,
        blockSel.Position.Z,
        byPlayer
      );

      return true;
    }
    return true;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var toggleHelp = new WorldInteraction
    {
      ActionLangCode = "smex:blockhelp-tap-toggle",
      MouseButton = EnumMouseButton.Right,
      // Toggling needs an empty hand (a held item is placed instead). Gate the
      // hint on that rather than RequireFreeHand, which would draw an empty slot.
      ShouldApply = (wi, bs, es) => forPlayer.Entity.RightHandItemSlot.Empty,
    };

    return baseHelp.Append(toggleHelp).ToArray();
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
  {
    return new ItemStack(
      world.GetBlock(new AssetLocation("smex", "blastfurnacetap-north")) ?? this
    );
  }

  public override ItemStack[] GetDrops(
    IWorldAccessor worldMap,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    return
    [
      new ItemStack(
        worldMap.GetBlock(new AssetLocation("smex", "blastfurnacetap-north"))
          ?? this
      ),
    ];
  }
}

using ExpandedLib.Registries.Entities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.Blocks.Structures;

/// <summary>
/// Generic behaviour shared by every multiblock anchor whose BE is a
/// <see cref="BlockEntityMultiblockStructure"/>. Centralises the build-outline projection:
/// Ctrl+Shift+right-click toggles the hologram of missing/incorrect blocks (routed to
/// <see cref="BlockEntityMultiblockStructure.Interact"/>) and contributes the help line. Both act
/// only while incomplete; once complete the projection auto-hides and the gesture passes through.
/// <para>
/// Add via <c>{ "name": "MultiblockStructure" }</c>, placed <b>before</b> any behaviour that also
/// consumes right-click so its <see cref="EnumHandling.PreventSubsequent"/> wins.
/// </para>
/// </summary>
[BlockBehaviorRegister("MultiblockStructure", PrefixModId = false)]
public class BlockBehaviorMultiblockStructure : BlockBehavior {
  public BlockBehaviorMultiblockStructure(Block block)
    : base(block) { }

  private static bool IsProjectionGesture(IPlayer byPlayer) {
    var controls = byPlayer?.Entity?.Controls;
    return controls != null && controls.CtrlKey && controls.ShiftKey;
  }

  private static BlockEntityMultiblockStructure? GetIncompleteStructure(
    IWorldAccessor world,
    BlockPos pos
  ) =>
    world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMultiblockStructure be
    && !be.StructureComplete
      ? be
      : null;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref EnumHandling handling
  ) {
    if (
      IsProjectionGesture(byPlayer)
      && GetIncompleteStructure(world, blockSel.Position) is { } be
    ) {
      be.Interact(byPlayer);
      (byPlayer as IClientPlayer)?.TriggerFpAnimation(
        EnumHandInteract.HeldItemInteract
      );
      handling = EnumHandling.PreventSubsequent;
      return true;
    }

    handling = EnumHandling.PassThrough;
    return false;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer,
    ref EnumHandling handling
  ) {
    if (GetIncompleteStructure(world, selection.Position) == null)
      return [];

    // Resolve the help text against the owning block's own domain so each mod shows
    // its own translation (both ppex and smex ship "blockhelp-mulblock-struc-show").
    return
    [
      new WorldInteraction
      {
        ActionLangCode = block.Code.Domain + ":blockhelp-mulblock-struc-show",
        HotKeyCodes = ["ctrl", "shift"],
        MouseButton = EnumMouseButton.Right,
      },
    ];
  }
}

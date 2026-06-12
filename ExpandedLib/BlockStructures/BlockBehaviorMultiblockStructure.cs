using ExpandedLib.EntityRegistry;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace ExpandedLib.BlockStructures;

/// <summary>
/// Generic block behaviour shared by every multiblock anchor block whose block entity
/// is a <see cref="BlockEntityMultiblockStructure"/>. It centralises the build-outline
/// projection so individual blocks don't each re-implement it:
/// <list type="bullet">
/// <item>Ctrl + Shift + right-click toggles the holographic projection of the still
/// missing/incorrect blocks (routed to <see cref="BlockEntityMultiblockStructure.Interact"/>).</item>
/// <item>It contributes the matching interaction-help line.</item>
/// </list>
/// Both only act while the structure is incomplete — once complete the projection
/// auto-hides (see <see cref="BlockEntityMultiblockStructure.FromTreeAttributes"/>), and
/// the gesture passes through so it never fights a block's own operational controls.
/// There is deliberately no separate "hide" gesture.
/// <para>
/// Add to a block JSON via <c>{ "name": "MultiblockStructure" }</c>. Place it
/// <b>before</b> any behaviour that also consumes right-click (e.g. the vanilla door)
/// so its <see cref="EnumHandling.PreventSubsequent"/> wins.
/// </para>
/// </summary>
[EntityRegister("MultiblockStructure", PrefixModId = false)]
public class BlockBehaviorMultiblockStructure : BlockBehavior
{
  public BlockBehaviorMultiblockStructure(Block block)
    : base(block) { }

  private static bool IsProjectionGesture(IPlayer byPlayer)
  {
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
  )
  {
    if (
      IsProjectionGesture(byPlayer)
      && GetIncompleteStructure(world, blockSel.Position) is { } be
    )
    {
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
  )
  {
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

using ExpandedLib;
using ExpandedLib.EntityRegistry;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// The Cornish engine mega-block (steel, high-pressure tier). Adds the steam control
/// rods: with a wrench in hand, right-click raises the steam-admission throttle one step
/// (low → normal → high) and ctrl + right-click lowers it (high → normal → low). Ctrl (not
/// sneak) is used for the lower direction because vanilla diverts a sneak + right-click
/// while holding an item to the held item — the wrench's own reverse-rotate would eat it
/// before the engine ever saw the click. The rods are reachable on the engine's own cell
/// and the filler cell directly above it. Repairs require steel only. All other behavior
/// lives in <see cref="BlockEngine"/>.
/// </summary>
[EntityRegister]
public class BlockEngineCornish : BlockEngine
{
  protected override RepairItem[] RepairItems =>
    [
      new(["metalplate-steel"], 4, "steel plate"),
      new(["rod-steel"], 2, "steel rod"),
    ];

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    // Direct click on the engine's own cell drives the control rods.
    if (TryThrottle(world, byPlayer, blockSel.Position, blockSel.Position))
      return true;

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  )
  {
    // A click forwarded from a footprint filler: the control rods only answer on the cell
    // directly above the engine (see TryThrottle); any other cell falls through to the base.
    if (TryThrottle(world, byPlayer, principalSel.Position, clickedCell))
      return true;

    return base.OnFillerInteractStart(
      world,
      byPlayer,
      principalSel,
      clickedCell
    );
  }

  /// <summary>
  /// Adjusts the steam control rods when a throttle cell — the engine's own cell or the
  /// filler directly above it — is clicked with a wrench: right-click raises the throttle
  /// one step, ctrl + right-click lowers it. Returns <c>true</c> when the click is
  /// consumed (so it isn't passed on to construction/repair handling). A broken engine
  /// answers nothing here, so the wrench click falls through to the base repair instead.
  /// </summary>
  private bool TryThrottle(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos enginePos,
    BlockPos clickedCell
  )
  {
    if (!IsThrottleCell(enginePos, clickedCell))
      return false;
    if (
      world.BlockAccessor.GetBlockEntity(enginePos)
        is not BlockEntityEngineCornish be
      || be.IsBroken
    )
      return false;

    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    if (held?.Collectible?.Code?.Path?.Contains("wrench") != true)
      return false;

    if (world.Side == EnumAppSide.Server)
    {
      var player = byPlayer as IServerPlayer;
      int direction = byPlayer.Entity.Controls.CtrlKey ? -1 : 1;
      if (be.AdjustThrottle(direction))
      {
        ExSounds.PlayAt(world, be.Pos, ExSounds.ToggleSwitch, byPlayer);
        player?.SendMessage(
          GlobalConstants.CurrentChatGroup,
          Lang.Get(
            "ppex:engine-throttle-set",
            Lang.Get("ppex:engine-throttle-" + be.ThrottleKey)
          ),
          EnumChatType.Notification
        );
      }
      else
      {
        // Already at the end of the range — tell the player which way it can't go.
        player?.SendIngameError(
          "ppex-engine",
          Lang.Get(
            direction > 0
              ? "ppex:engine-throttle-max"
              : "ppex:engine-throttle-min"
          )
        );
      }
    }
    return true;
  }

  /// <summary>The control rods are reachable on the engine's own cell and the filler directly above it.</summary>
  private static bool IsThrottleCell(
    BlockPos enginePos,
    BlockPos clickedCell
  ) => clickedCell.Equals(enginePos) || clickedCell.Equals(enginePos.UpCopy());

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) =>
    WithThrottleHelp(
      world,
      selection.Position,
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
    );

  public override WorldInteraction[] GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  )
  {
    WorldInteraction[] help = base.GetFillerInteractionHelp(
      world,
      principalSel,
      forPlayer,
      clickedCell
    );
    // Only the filler directly above the engine carries the control rods.
    if (!clickedCell.Equals(principalSel.Position.UpCopy()))
      return help;
    return WithThrottleHelp(world, principalSel.Position, help);
  }

  /// <summary>
  /// Appends the throttle raise/lower wrench actions to <paramref name="baseHelp"/> when
  /// the engine at <paramref name="enginePos"/> is constructed and intact (a broken engine
  /// only shows the repair action, which the base already supplies).
  /// </summary>
  private WorldInteraction[] WithThrottleHelp(
    IWorldAccessor world,
    BlockPos enginePos,
    WorldInteraction[] baseHelp
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(enginePos)
        is not BlockEntityEngineCornish be
      || !be.IsConstructed
      || be.IsBroken
    )
      return baseHelp;

    ItemStack[] wrench = ExItems.WrenchStacks(world);
    WorldInteraction raise = new()
    {
      ActionLangCode = "ppex:blockhelp-engine-throttle-up",
      MouseButton = EnumMouseButton.Right,
      Itemstacks = wrench,
    };
    WorldInteraction lower = new()
    {
      ActionLangCode = "ppex:blockhelp-engine-throttle-down",
      MouseButton = EnumMouseButton.Right,
      HotKeyCode = "ctrl",
      Itemstacks = wrench,
    };
    return [.. baseHelp, raise, lower];
  }
}

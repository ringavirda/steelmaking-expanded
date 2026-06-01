using System.Linq;
using SteelmakingExpanded.Structures.BessemerConverter.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace SteelmakingExpanded.Structures.BessemerConverter.Blocks;

/// <summary>
/// The bessemer control block — the operator interface and "brain" anchor of the
/// converter multiblock. Routes player input to the <see cref="BlockEntityBessemerControl"/>:
/// inspecting the structure while building, and selecting the Normal/Filling/Pouring
/// state once complete.
/// </summary>
public class BlockBessemerControl : Block
{
  /// <summary>
  /// Pouring empties the whole charge, so it must be held — not single-clicked —
  /// to fire, guarding against accidental pours. The hold time, in seconds.
  /// </summary>
  private const float PourHoldSeconds = 1f;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityBessemerControl be
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    var controls = byPlayer.Entity.Controls;

    // Ctrl inspects the multiblock — but only while still building. Once the
    // structure is complete the Sprint key (which shares Ctrl) selects Pouring,
    // so don't intercept it for inspection anymore.
    if (controls.CtrlKey && !be.StructureComplete)
    {
      be.Interact(byPlayer);
      return true;
    }

    // Before the converter exists: RMB tries to spawn it from carried materials.
    if (!be.IsConverterPresent())
    {
      if (be.TrySpawnConverter(byPlayer, out string spawnError))
      {
        (byPlayer as IClientPlayer)?.TriggerFpAnimation(
          EnumHandInteract.HeldItemInteract
        );
      }
      else if (world.Side == EnumAppSide.Client && spawnError.Length > 0)
      {
        (byPlayer as IClientPlayer)?.ShowChatNotification(spawnError);
      }
      return true;
    }

    BessemerOpState target = ResolveTarget(byPlayer);

    // Pouring is destructive (drains the entire charge), so rather than firing on
    // the click we begin a hold here and only commit once PourHoldSeconds have
    // elapsed in OnBlockInteractStep. Validate up front so the player isn't left
    // holding a doomed interaction; surface the same error a click would.
    if (target == BessemerOpState.Pouring)
    {
      if (be.OpState == BessemerOpState.Pouring)
        return false; // already pouring — nothing to hold for
      if (!be.CanOperate(out string pourError))
      {
        if (world.Side == EnumAppSide.Client && pourError.Length > 0)
          (byPlayer as IClientPlayer)?.ShowChatNotification(pourError);
        return false;
      }
      (byPlayer as IClientPlayer)?.TriggerFpAnimation(
        EnumHandInteract.HeldItemInteract
      );
      return true; // continue into OnBlockInteractStep
    }

    // Normal / Filling are non-destructive — apply immediately on click.
    if (be.TrySetState(byPlayer, target, out string error))
    {
      (byPlayer as IClientPlayer)?.TriggerFpAnimation(
        EnumHandInteract.HeldItemInteract
      );
    }
    else if (world.Side == EnumAppSide.Client && error.Length > 0)
    {
      (byPlayer as IClientPlayer)?.ShowChatNotification(error);
    }
    return true;
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityBessemerControl be
    )
      return false;

    // Only the pour action is a held interaction. Stop the moment the player lets
    // go of sprint, the converter is gone, or it is already pouring.
    if (
      !be.IsConverterPresent()
      || ResolveTarget(byPlayer) != BessemerOpState.Pouring
      || be.OpState == BessemerOpState.Pouring
    )
      return false;

    if (secondsUsed < PourHoldSeconds)
      return true; // keep holding

    // Held long enough — commit the pour (TrySetState re-validates internally).
    be.TrySetState(byPlayer, BessemerOpState.Pouring, out _);
    return false;
  }

  // Operational intent from the held modifier keys: Sneak = filling,
  // Sprint = pouring, plain RMB = normal.
  private static BessemerOpState ResolveTarget(IPlayer byPlayer)
  {
    var controls = byPlayer.Entity.Controls;
    return controls.Sneak ? BessemerOpState.Filling
      : controls.Sprint ? BessemerOpState.Pouring
      : BessemerOpState.Normal;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    if (
      world.BlockAccessor.GetBlockEntity(selection.Position)
      is not BlockEntityBessemerControl be
    )
      return baseHelp;

    // Construction phase: converter-spawn hint + structure highlight.
    if (!be.IsConverterPresent())
    {
      return baseHelp
        .Append(
          new WorldInteraction
          {
            ActionLangCode = "smex:blockhelp-bessemer-spawn",
            MouseButton = EnumMouseButton.Right,
          }
        )
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
        )
        .ToArray();
    }

    // Operational phase: state transition hints.
    return baseHelp
      .Append(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-bessemer-normal",
          MouseButton = EnumMouseButton.Right,
        }
      )
      .Append(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-bessemer-filling",
          HotKeyCodes = ["sneak"],
          MouseButton = EnumMouseButton.Right,
        }
      )
      .Append(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-bessemer-pouring",
          HotKeyCodes = ["sprint"],
          MouseButton = EnumMouseButton.Right,
        }
      )
      .ToArray();
  }
}

using System.Collections.Generic;
using ExpandedLib;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockNetworkMolten.Blocks;

/// <summary>
/// Canal tap: pours the network's liquid metal downward into a molten barrel or a
/// large tool mold placed beneath it. Sneak + right-click adds/removes the
/// barrel/mold; Ctrl + right-click toggles pouring on and off.
/// </summary>
[EntityRegister]
public class BlockMoltenCanalTap : BlockMoltenCanal
{
  // Barrel + large molds (anvil, helve hammer) that can be cast in the tap.
  private ItemStack[]? _acceptedContents;

  public override void OnLoaded(ICoreAPI api)
  {
    base.OnLoaded(api);

    var list = new List<ItemStack>();
    var barrel = api.World.GetBlock(new AssetLocation("smex:moltenbarrel"));
    if (barrel != null)
      list.Add(new ItemStack(barrel));
    foreach (var block in api.World.Blocks)
      if (MoldKinds.IsLarge(block))
        list.Add(new ItemStack(block));
    _acceptedContents = list.ToArray();
  }

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityMoltenCanalTap be
    )
      return false;

    // Sneak (ShiftKey) + RMB places/removes the barrel; the opposite modifier
    // (CtrlKey) + RMB toggles pouring. Plain RMB is left unhandled.
    bool sneak = byPlayer.Entity.Controls.ShiftKey;
    bool opposite = byPlayer.Entity.Controls.CtrlKey;
    if (!sneak && !opposite)
      return false;

    if (world.Side == EnumAppSide.Client)
      return true;

    if (sneak)
    {
      Block blockBelow = world.BlockAccessor.GetBlock(
        blockSel.Position.DownCopy()
      );
      if (!blockBelow.SideSolid[BlockFacing.UP.Index])
        return false;

      if (be.HasContent)
      {
        // A mold full of still-liquid metal may only be taken into an empty
        // hand — anywhere else in the inventory it instantly spills.
        bool liquidMold =
          !be.IsBarrel
          && MoltenMoldSpill.IsLiquidContent(
            world,
            be.MoldMetalContent,
            be.MoldCurrentUnits
          );
        if (
          liquidMold
          && MoltenMoldSpill.DenyLiquidPickup(
            world,
            byPlayer,
            be.MoldMetalContent,
            be.MoldCurrentUnits
          )
        )
          return true;

        var stack = be.IsBarrel ? be.RemoveBarrel() : be.RemoveMold();
        MoltenMoldSpill.GiveMoldStack(
          world,
          byPlayer,
          stack,
          liquidMold,
          blockSel.Position.ToVec3d().Add(0.5, 1.0, 0.5)
        );
      }
      else
      {
        var heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (heldSlot?.Itemstack is not { } heldStack)
          return false;

        if (heldStack.Block is BlockMoltenBarrel)
        {
          be.AddBarrel(heldStack);
        }
        else if (MoldKinds.IsLarge(heldStack.Block))
        {
          be.AddMold(heldStack);
        }
        else if (heldStack.Block is BlockToolMold)
        {
          (byPlayer as IServerPlayer)?.SendIngameError("smex-moldtoosmall");
          return false;
        }
        else
        {
          return false;
        }

        heldSlot.TakeOut(1);
        heldSlot.MarkDirty();
      }
      ExSounds.Play(world.Api, blockSel.Position, ExSounds.Ingot, 0.7f);
      be.MarkDirty(true);
    }
    else
    {
      be.TryTogglePouring();
    }

    return true;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    var interactions =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    bool hasContent =
      world.BlockAccessor.GetBlockEntity(selection.Position)
        is BlockEntityMoltenCanalTap be
      && be.HasContent;

    var result = new List<WorldInteraction>(interactions)
    {
      new()
      {
        ActionLangCode = "smex:blockhelp-canal-togglepour",
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "sprint",
      },
    };

    Block blockBelow = world.BlockAccessor.GetBlock(
      selection.Position.DownCopy()
    );
    if (!blockBelow.SideSolid[BlockFacing.UP.Index])
      return result.ToArray();

    if (!hasContent)
    {
      result.Add(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-tap-placecontent",
          MouseButton = EnumMouseButton.Right,
          HotKeyCode = "sneak",
          Itemstacks = _acceptedContents,
        }
      );
    }
    else
    {
      result.Add(
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-tap-removecontent",
          MouseButton = EnumMouseButton.Right,
          HotKeyCode = "sneak",
        }
      );
    }

    return result.ToArray();
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    // A parked barrel/mold is stored on the BE (the player's item was consumed on
    // placement), not as a separate block, so drop it — with its contents — before
    // the tap is removed, otherwise it is silently lost.
    if (
      world.Side == EnumAppSide.Server
      && byPlayer is not { WorldData.CurrentGameMode: EnumGameMode.Creative }
      && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanalTap be
      && be.HasContent
    )
    {
      ItemStack? parked = be.IsBarrel
        ? be.RemoveBarrel()
        : (be.MoldStack != null ? be.RemoveMold() : null);
      if (parked != null)
        world.SpawnItemEntity(parked, pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "tap", ["n", "s", "w", "e"] } };

  protected override string GetFallbackOrientation(string? type) =>
    type switch
    {
      "tap" => "n",
      _ => "n",
    };

  protected override void GetRotations(
    string orientation,
    out float rotX,
    out float rotY,
    out float rotZ
  )
  {
    rotX = 0;
    rotY = 0;
    rotZ = 0;

    switch (orientation)
    {
      case "n":
        rotY = 0;
        break;
      case "s":
        rotY = 180;
        break;
      case "w":
        rotY = 90;
        break;
      case "e":
        rotY = 270;
        break;
      default:
        base.GetRotations(orientation, out rotX, out rotY, out rotZ);
        break;
    }
  }
}

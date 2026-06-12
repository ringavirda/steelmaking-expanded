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
/// Mold pedestal: a canal endpoint that holds a small tool mold and fills it from
/// the network's liquid metal. Sneak + right-click places/removes the mold;
/// Ctrl + right-click toggles pouring.
/// </summary>
[EntityRegister]
public class BlockMoltenCanalMoldPedestal : BlockMoltenCanalTap
{
  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new() { { "moldpedestal", ["n", "s", "w", "e"] } };

  private ItemStack[]? _acceptedMolds;

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    // The placed mold (and any cast metal) lives on the BE, not as a separate
    // block, so drop it before the pedestal is removed or it is silently lost.
    if (
      world.Side == EnumAppSide.Server
      && byPlayer is not { WorldData.CurrentGameMode: EnumGameMode.Creative }
      && world.BlockAccessor.GetBlockEntity(pos)
        is BlockEntityMoltenCanalMoldPedestal be
      && be.IsMold
      && be.MoldStack != null
    )
    {
      world.SpawnItemEntity(be.RemoveMold(), pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  public override void OnLoaded(ICoreAPI api)
  {
    base.OnLoaded(api);

    var moldList = new List<ItemStack>();
    foreach (var block in api.World.Blocks)
    {
      if (MoldKinds.FitsPedestal(block))
        moldList.Add(new ItemStack(block));
    }
    _acceptedMolds = moldList.ToArray();
  }

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityMoltenCanalMoldPedestal be
    )
      return false;

    // Sneak (ShiftKey) + RMB places/removes the mold; the opposite modifier
    // (CtrlKey) + RMB toggles pouring. Plain RMB is left unhandled.
    bool sneak = byPlayer.Entity.Controls.ShiftKey;
    bool opposite = byPlayer.Entity.Controls.CtrlKey;
    if (!sneak && !opposite)
      return false;

    if (world.Side == EnumAppSide.Client)
      return true;

    if (opposite)
    {
      be.TryTogglePouring();
      return true;
    }

    if (!be.IsMold)
    {
      var heldSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
      if (heldSlot?.Itemstack?.Block is not BlockToolMold)
        return false;

      if (!MoldKinds.FitsPedestal(heldSlot.Itemstack.Block))
      {
        (byPlayer as IServerPlayer)?.SendIngameError("smex-moldtoolarge");
        return false;
      }

      be.AddMold(heldSlot.Itemstack);
      heldSlot.TakeOut(1);
      heldSlot.MarkDirty();
    }
    else
    {
      // A mold full of still-liquid metal may only be taken into an empty
      // hand — anywhere else in the inventory it instantly spills.
      bool liquidMold = MoltenMoldSpill.IsLiquidContent(
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

      var moldStack = be.RemoveMold();
      MoltenMoldSpill.GiveMoldStack(
        world,
        byPlayer,
        moldStack,
        liquidMold,
        blockSel.Position.ToVec3d().Add(0.5, 1.0, 0.5)
      );
    }
    ExSounds.Play(world.Api, blockSel.Position, ExSounds.Ingot, 0.7f);
    be.MarkDirty(true);
    return true;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    bool isMold =
      world.BlockAccessor.GetBlockEntity(selection.Position)
      is BlockEntityMoltenCanalMoldPedestal { IsMold: true };

    var toggle = new WorldInteraction
    {
      ActionLangCode = "smex:blockhelp-canal-togglepour",
      MouseButton = EnumMouseButton.Right,
      HotKeyCode = "sprint",
    };

    if (!isMold)
      return
      [
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-pedestal-placemold",
          MouseButton = EnumMouseButton.Right,
          HotKeyCode = "sneak",
          Itemstacks = _acceptedMolds,
        },
        toggle,
      ];

    return
    [
      new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-pedestal-removemold",
        MouseButton = EnumMouseButton.Right,
        HotKeyCode = "sneak",
      },
      toggle,
    ];
  }
}

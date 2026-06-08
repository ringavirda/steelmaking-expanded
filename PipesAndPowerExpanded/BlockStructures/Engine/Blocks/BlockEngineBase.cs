using System;
using System.Collections.Generic;
using System.Linq;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockStructures.Engine.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace PipesAndPowerExpanded.BlockStructures.Engine.Blocks;

/// <summary>
/// Shared base for the steam engine mega-blocks. Each occupies a single grid cell but
/// renders across its footprint, which is reserved with invisible structure fillers
/// (see <see cref="StructureFillers"/>). Construction is driven by the
/// RightClickConstructable block-entity behavior declared in the block JSON.
/// <para>
/// In its default (north) orientation it exposes pipe connectors on the south face
/// (steam intake) and the east face (condensed water out); both rotate with the block.
/// </para>
/// </summary>
public abstract class BlockEngineBase : Block, INetworkConnector
{
  // Pipe ports in north orientation, rotated to the placed orientation at runtime.
  private static readonly BlockFacing[] BaseConnectorFaces =
  [
    BlockFacing.SOUTH, // steam intake
    BlockFacing.EAST, // condensed water out
  ];

  // The structure body extends along the local +z ("south") axis. Offset the
  // placement angle by 180° so HorizontalOrientable raises it AWAY from the player
  // instead of into them; the JSON rotateYByType is offset to match so the visual,
  // fillers and connectors stay aligned.
  protected int Angle =>
    (StructureFillers.AngleFromSide(Variant["side"]) + 180) % 360;

  public string NetworkType => "pipe";

  public bool HasConnectorAt(BlockFacing face)
  {
    int angle = Angle;
    foreach (var baseFace in BaseConnectorFaces)
    {
      if (StructureFillers.RotateFacing(baseFace, angle) == face)
        return true;
    }
    return false;
  }

  /// <summary>World cell of the steam-inlet pipe (local-south face, rotated).</summary>
  public BlockPos SteamInletPos(BlockPos enginePos) =>
    enginePos.AddCopy(StructureFillers.RotateFacing(BlockFacing.SOUTH, Angle));

  /// <summary>World cell of the condensed-water-out pipe (local-east face, rotated).</summary>
  public BlockPos WaterOutletPos(BlockPos enginePos) =>
    enginePos.AddCopy(StructureFillers.RotateFacing(BlockFacing.EAST, Angle));

  /// <summary>World cell of the attached sub-machine (local {0,0,2}, rotated).</summary>
  public BlockPos SubmachinePos(BlockPos enginePos)
  {
    Vec3i r = StructureFillers.RotateOffset(new Vec3i(0, 0, 2), Angle);
    return enginePos.AddCopy(r.X, r.Y, r.Z);
  }

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    var cells = StructureFillers.FootprintCells(this, blockSel.Position, Angle);
    if (!StructureFillers.CanPlace(world, cells))
    {
      failureCode = "notenoughspace";
      return false;
    }
    return true;
  }

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  )
  {
    base.OnBlockPlaced(world, blockPos, byItemStack);
    StructureFillers.PlaceFillers(
      world,
      blockPos,
      StructureFillers.FootprintCells(this, blockPos, Angle)
    );
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  )
  {
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells(this, pos, Angle)
    );
    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  #region Repair

  /// <summary>A material a broken engine needs to be repaired: any of <see cref="Codes"/>
  /// (matched by item code path), a required <see cref="Quantity"/>, and a display name.</summary>
  protected readonly record struct RepairItem(
    string[] Codes,
    int Quantity,
    string Display
  );

  /// <summary>Materials a wrench-repair of this engine consumes (steel-only for Cornish; iron or steel for Watt).</summary>
  protected abstract RepairItem[] RepairItems { get; }

  /// <summary>Human-readable list of the repair materials, for the broken-engine HUD line.</summary>
  public string RepairDescription =>
    string.Join(", ", RepairItems.Select(r => $"{r.Quantity}× {r.Display}"));

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    // A broken engine only responds to a wrench repair until it is fixed.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is BlockEntityEngineBase be
      && be.IsBroken
    )
    {
      if (world.Side == EnumAppSide.Server)
        TryRepair(world, byPlayer, be);
      return true;
    }

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  /// <summary>Server-side: with a wrench in hand and the materials in inventory, fixes the engine.</summary>
  private void TryRepair(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockEntityEngineBase be
  )
  {
    var player = byPlayer as IServerPlayer;
    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;

    if (slot?.Itemstack?.Collectible?.Code?.Path?.Contains("wrench") != true)
    {
      player?.SendIngameError("ppex-engine", Lang.Get("ppex:engine-repair-wrench"));
      return;
    }

    var missing = RepairItems
      .Where(r => CountMatching(byPlayer, r.Codes) < r.Quantity)
      .Select(r => $"{r.Quantity}× {r.Display}")
      .ToList();
    if (missing.Count > 0)
    {
      player?.SendIngameError(
        "ppex-engine",
        Lang.Get("ppex:engine-repair-missing", string.Join(", ", missing))
      );
      return;
    }

    foreach (var r in RepairItems)
      TakeMatching(byPlayer, r.Codes, r.Quantity);

    be.Repair();
    world.PlaySoundAt(
      new AssetLocation("game:sounds/block/meposthit"),
      be.Pos.X + 0.5,
      be.Pos.Y + 0.5,
      be.Pos.Z + 0.5,
      byPlayer
    );
    player?.SendMessage(
      GlobalConstants.CurrentChatGroup,
      Lang.Get("ppex:engine-repaired"),
      EnumChatType.Notification
    );
  }

  private static bool Matches(ItemStack stack, string[] codes) =>
    stack.Collectible?.Code != null
    && codes.Contains(stack.Collectible.Code.Path);

  private static int CountMatching(IPlayer player, string[] codes)
  {
    int count = 0;
    player.Entity.WalkInventory(slot =>
    {
      if (slot?.Itemstack != null && Matches(slot.Itemstack, codes))
        count += slot.Itemstack.StackSize;
      return true;
    });
    return count;
  }

  private static void TakeMatching(IPlayer player, string[] codes, int qty)
  {
    int remaining = qty;
    player.Entity.WalkInventory(slot =>
    {
      if (remaining <= 0)
        return false;
      if (slot?.Itemstack != null && Matches(slot.Itemstack, codes))
      {
        int take = Math.Min(remaining, slot.Itemstack.StackSize);
        slot.TakeOut(take);
        slot.MarkDirty();
        remaining -= take;
      }
      return remaining > 0;
    });
  }

  #endregion
}

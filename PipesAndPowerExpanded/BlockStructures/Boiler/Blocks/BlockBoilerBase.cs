using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockStructures.Boiler.BlockEntities;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Boiler.Blocks;

/// <summary>
/// Shared base for the boiler mega-blocks. Each occupies a single grid cell but
/// renders across a multi-cell volume that is reserved with invisible structure
/// fillers (see <see cref="StructureFillers"/>) so the player gets real collision
/// over the whole boiler. Construction is driven by the RightClickConstructable
/// block-entity behavior declared in the block JSON.
/// </summary>
public abstract class BlockBoilerBase : Block, INetworkConnector
{
  // The structure body extends along the local +z ("south") axis. Offset the
  // placement angle by 180° so HorizontalOrientable raises it AWAY from the player
  // instead of into them; the JSON rotateYByType is offset to match so the visual,
  // fillers and connectors stay aligned.
  private int Angle =>
    (StructureFillers.AngleFromSide(Variant["side"]) + 180) % 360;

  /// <summary>The structure/filler rotation angle, for multiblockStructure verification.</summary>
  public int StructureAngle => Angle;

  // The boiler draws water through a pipe on its bottom face; steam and exhaust leave
  // through their dedicated outlet blocks placed at the cells named by the attributes.
  public string NetworkType => "pipe";

  public bool HasConnectorAt(BlockFacing face) => face == BlockFacing.DOWN;

  private BlockPos OffsetWorldPos(
    BlockPos boilerPos,
    string attr,
    Vec3i fallback
  )
  {
    var node = Attributes?[attr];
    Vec3i off =
      node == null || !node.Exists
        ? fallback
        : new Vec3i(node["x"].AsInt(), node["y"].AsInt(), node["z"].AsInt());
    Vec3i r = StructureFillers.RotateOffset(off, Angle);
    return boilerPos.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>
  /// World cell of the firebox slot (<c>fuelOffset</c> attribute, default {0,0,-1});
  /// the player places and lights a coal pile here, behind the coke-oven door.
  /// </summary>
  public BlockPos FuelWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "fuelOffset", new Vec3i(0, 0, -1));

  /// <summary>
  /// World cell of the exhaust gas outlet (<c>gasOutletOffset</c> attribute,
  /// default {0,1,4}); the boiler vents exhaust into the pipe network there.
  /// </summary>
  public BlockPos PipeOutletWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "gasOutletOffset", new Vec3i(0, 1, 4));

  /// <summary>
  /// World position of the cell where the steam outlet attaches, derived from the
  /// block's <c>outletOffset</c> attribute and current orientation. This cell is
  /// deliberately left out of the filler footprint so the outlet can be placed there.
  /// </summary>
  public BlockPos OutletWorldPos(BlockPos boilerPos)
  {
    var node = Attributes?["outletOffset"];
    Vec3i off =
      node == null || !node.Exists
        ? new Vec3i(0, 0, 2)
        : new Vec3i(node["x"].AsInt(), node["y"].AsInt(), node["z"].AsInt());
    Vec3i r = StructureFillers.RotateOffset(off, Angle);
    return boilerPos.AddCopy(r.X, r.Y, r.Z);
  }

  /// <summary>Removes the boiler's reserved filler footprint (used by the explosion path).</summary>
  public void RemoveStructure(IWorldAccessor world, BlockPos pos) =>
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells(this, pos, Angle)
    );

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // The boiler fills a multi-cell volume; refuse placement unless that whole volume
    // is clear, otherwise the fillers would silently fail to spawn.
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
    // Clear the reserved volume first so a thrown construction-drop path can't
    // leave invisible solid cells behind.
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells(this, pos, Angle)
    );

    // Release the linked steam outlet so it no longer points at a dead boiler.
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityBoilerBase be
      && be.OutletPos != null
    )
    {
      // Leave the outlet block in place; just drop the link on the boiler side.
      be.LinkOutlet(null);
    }

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  #region Lid interactions

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityBoilerBase be
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    // Ctrl+Shift is the shared structure-projection gesture (handled by the
    // MultiblockStructure block behavior). Defer to base so that behavior — not the
    // lid sprint handling below — receives the click.
    if (byPlayer.Entity.Controls.CtrlKey && byPlayer.Entity.Controls.ShiftKey)
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    var ctrl = byPlayer.Entity.Controls;
    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;

    // Sneak + a water container while the lid is open → manual kickstart fill.
    if (ctrl.Sneak && be.LidOpen && IsWaterContainer(slot?.Itemstack))
    {
      if (world.Side == EnumAppSide.Server && slot != null)
        be.TryManualFill(byPlayer, slot);
      return true;
    }

    // Sprint → begin the 1 s hold that toggles the lid.
    if (ctrl.Sneak)
      return true;

    return base.OnBlockInteractStart(world, byPlayer, blockSel);
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) => byPlayer.Entity.Controls.Sprint && secondsUsed < 1.0f;

  public override void OnBlockInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      secondsUsed >= 1.0f
      && world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityBoilerBase be
    )
      be.ToggleLid();
  }

  private static bool IsWaterContainer(ItemStack? stack)
  {
    if (stack?.Collectible is not BlockLiquidContainerBase cont)
      return false;
    ItemStack? content = cont.GetContent(stack);
    return content?.Collectible?.Code?.Path?.Contains("water") == true;
  }

  #endregion
}

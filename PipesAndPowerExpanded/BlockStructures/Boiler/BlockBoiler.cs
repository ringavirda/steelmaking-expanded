using System.Collections.Generic;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Shared base for the boiler mega-blocks. Each occupies a single grid cell but
/// renders across a multi-cell volume that is reserved with invisible structure
/// fillers (see <see cref="StructureFillers"/>) so the player gets real collision
/// over the whole boiler. Construction is driven by the RightClickConstructable
/// block-entity behavior declared in the block JSON.
/// </summary>
public abstract class BlockBoiler
  : Block,
    INetworkConnector,
    IFillerInteractionTarget
{
  // The structure body extends along the local +z ("south") axis. Offset the
  // placement angle by 180° so HorizontalOrientable raises it AWAY from the player
  // instead of into them; the JSON rotateYByType is offset to match so the visual,
  // fillers and connectors stay aligned.
  private int Angle =>
    (ExOrientation.AngleFromSide(Variant["side"]) + 180) % 360;

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
  ) => ExOrientation.WorldPosFromAttr(this, boilerPos, attr, fallback, Angle);

  /// <summary>
  /// World cell of the firebox slot.
  /// </summary>
  public BlockPos FuelWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "fuelOffset", new Vec3i(0, 0, -1));

  /// <summary>
  /// World cell of the exhaust gas outlet.
  /// </summary>
  public BlockPos ExhaustOutletWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "exhaustOutletOffset", new Vec3i(0, 1, 4));

  /// <summary>
  /// World cell of the filler that carries the access lid.
  /// </summary>
  public BlockPos LidWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "lidOffset", new Vec3i(0, 1, 0));

  /// <summary>
  /// World cell of the boiler's steam connector (the port filler at the top of the body); the
  /// steam pipe attaches in the cell directly above it. Rotated to the current orientation.
  /// </summary>
  public BlockPos SteamPipeWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "steamConnectorOffset", new Vec3i(0, 1, 2));

  /// <summary>
  /// World cell the animated vessel mesh is lit from. The boiler renders its whole footprint
  /// through one animator, which vanilla lights from the block's own (firebox-adjacent) cell —
  /// so the burning firebox tints the entire vessel red at night. This points the light sample
  /// at a cell on the vessel body instead (default the top-rear of the body, sky-exposed and
  /// well clear of the firebox). Read from <c>lightSampleOffset</c> (local), rotated by angle.
  /// </summary>
  public BlockPos LightSampleWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "lightSampleOffset", new Vec3i(0, 1, 2));

  /// <summary>
  /// World cell at the centre of the boiler's footprint (the vessel body mid-point), where
  /// the burst explosion is centred so it goes off inside the boiler rather than off to the
  /// firebox side. Read from <c>explosionCenterOffset</c> (local), rotated by the structure
  /// angle.
  /// </summary>
  public BlockPos ExplosionCenterPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, "explosionCenterOffset", new Vec3i(0, 1, 1));

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
    MarkSteamPort(world, blockPos);
  }

  /// <summary>
  /// Turns the steam-connector filler cell (<see cref="SteamPipeWorldPos"/>, the top of the
  /// boiler body) into an upward "pipe" port, so a steam pipe placed directly above it
  /// connects (auto-orients down, no leak) straight into the boiler instead of dangling over
  /// an inert filler.
  /// </summary>
  private void MarkSteamPort(IWorldAccessor world, BlockPos boilerPos)
  {
    if (world.Side != EnumAppSide.Server)
      return;
    BlockPos portCell = SteamPipeWorldPos(boilerPos);
    if (
      world.BlockAccessor.GetBlockEntity(portCell)
      is BlockEntityStructureFiller be
    )
    {
      be.PortFace = "u";
      be.PortNetworkType = NetworkType; // "pipe"
      be.MarkDirty(true);
    }
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

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  #region Lid interactions

  // The lid and the manual water fill live on one specific footprint cell — the filler
  // that carries the lid (LidWorldPos). Interactions arrive either directly on the
  // boiler's own cell (the Block.OnBlockInteract* overrides) or forwarded from a filler
  // (the IFillerInteractionTarget implementation); both funnel into the Handle* helpers
  // below, which gate the lid handling on the clicked cell being the lid cell and
  // otherwise return null/false to defer to the default block behavior (construction,
  // structure projection).

  /// <summary>Hold duration (seconds) required to toggle the lid open or closed.</summary>
  private const float LidHoldSeconds = 0.5f;

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    HandleInteractStart(world, byPlayer, blockSel, blockSel.Position)
    ?? base.OnBlockInteractStart(world, byPlayer, blockSel);

  bool IFillerInteractionTarget.OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) =>
    HandleInteractStart(world, byPlayer, principalSel, clickedCell)
    ?? base.OnBlockInteractStart(world, byPlayer, principalSel);

  /// <summary>
  /// Shared lid/fill click logic. <paramref name="sel"/> points at the boiler's own
  /// cell (for block-entity lookup); <paramref name="clickedCell"/> is the cell the
  /// player actually looked at. Returns <c>null</c> to defer to the default block
  /// behavior, or an explicit handled result.
  /// </summary>
  private bool? HandleInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel,
    BlockPos clickedCell
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
      is not BlockEntityBoiler be
    )
      return null;

    // Ctrl+Shift is the shared structure-projection gesture; pre-construction clicks
    // drive RightClickConstructable. Both belong to the default block behavior.
    if (byPlayer.Entity.Controls.CtrlKey && byPlayer.Entity.Controls.ShiftKey)
      return null;
    if (!be.IsConstructed)
      return null;

    // The lid and manual fill only respond on the lid-bearing cell.
    if (!clickedCell.Equals(LidWorldPos(sel.Position)))
      return null;

    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;

    // A water container while the lid is open → pour its entire contents in.
    if (be.LidOpen && IsWaterContainer(slot?.Itemstack))
    {
      if (world.Side == EnumAppSide.Server && slot != null)
        be.TryManualFill(byPlayer, slot);
      return true;
    }

    // Empty hands → begin the lid hold (open AND close both need the hold). The
    // toggle itself happens in the step loop once the hold passes the threshold.
    if (slot?.Empty != false)
    {
      be.LidToggled = false;
      return true;
    }

    return null;
  }

  public override bool OnBlockInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) =>
    HandleInteractStep(
      secondsUsed,
      world,
      byPlayer,
      blockSel,
      blockSel.Position
    ) ?? base.OnBlockInteractStep(secondsUsed, world, byPlayer, blockSel);

  bool IFillerInteractionTarget.OnFillerInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) =>
    HandleInteractStep(secondsUsed, world, byPlayer, principalSel, clickedCell)
    ?? base.OnBlockInteractStep(secondsUsed, world, byPlayer, principalSel);

  private bool? HandleInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel,
    BlockPos clickedCell
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
        is not BlockEntityBoiler be
      || !be.IsConstructed
      || !clickedCell.Equals(LidWorldPos(sel.Position))
    )
      return null;

    // Only the empty-handed lid hold uses the step loop; a held item (e.g. the pour)
    // ends the interaction immediately.
    if (byPlayer.InventoryManager?.ActiveHotbarSlot?.Empty != true)
      return false;

    // Toggle exactly once when the hold passes the threshold, then keep the
    // interaction alive until the button is released. Holding the button open keeps
    // returning true so the engine does not restart the interaction (which would
    // toggle the lid again and again).
    if (secondsUsed >= LidHoldSeconds && !be.LidToggled)
    {
      be.LidToggled = true;
      if (world.Side == EnumAppSide.Server)
        be.ToggleLid();
    }

    return true;
  }

  public override void OnBlockInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (!HandleInteractStop(world, byPlayer, blockSel))
      base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
  }

  void IFillerInteractionTarget.OnFillerInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  )
  {
    if (!HandleInteractStop(world, byPlayer, principalSel))
      base.OnBlockInteractStop(secondsUsed, world, byPlayer, principalSel);
  }

  private bool HandleInteractStop(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
        is not BlockEntityBoiler be
      || !be.IsConstructed
    )
      return false;

    be.LidToggled = false;
    return true;
  }

  private static bool IsWaterContainer(ItemStack? stack)
  {
    if (stack?.Collectible is not BlockLiquidContainerBase cont)
      return false;
    ItemStack? content = cont.GetContent(stack);
    return content?.Collectible?.Code?.Path?.Contains("water") == true;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) => HandleInteractionHelp(world, selection, forPlayer, selection.Position);

  WorldInteraction[] IFillerInteractionTarget.GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  ) => HandleInteractionHelp(world, principalSel, forPlayer, clickedCell);

  private WorldInteraction[] HandleInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer,
    BlockPos clickedCell
  )
  {
    var help = new List<WorldInteraction>(
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? []
    );

    // The lid hints only show on a finished vessel, and only when looking at the
    // lid-bearing cell.
    if (
      world.BlockAccessor.GetBlockEntity(selection.Position)
        is not BlockEntityBoiler be
      || !be.IsConstructed
      || !clickedCell.Equals(LidWorldPos(selection.Position))
    )
      return help.ToArray();

    // Empty-handed right click toggles the lid.
    help.Add(
      new WorldInteraction
      {
        ActionLangCode = "ppex:blockhelp-boiler-lid",
        MouseButton = EnumMouseButton.Right,
        RequireFreeHand = true,
      }
    );

    // The manual water fill only works while the lid is open, so only advertise it then.
    if (be.LidOpen)
      help.Add(
        new WorldInteraction
        {
          ActionLangCode = "ppex:blockhelp-boiler-fill",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = WaterContainerStacks(world),
        }
      );

    return help.ToArray();
  }

  /// <summary>Water-filled liquid containers shown on the manual-fill interaction hint, resolved once.</summary>
  private static ItemStack[]? _waterContainerStacks;

  private static ItemStack[] WaterContainerStacks(IWorldAccessor world)
  {
    if (_waterContainerStacks != null)
      return _waterContainerStacks;

    var waterStack = new ItemStack(
      world.GetItem(new AssetLocation("game:waterportion"))
    );
    var list = new List<ItemStack>();
    foreach (var block in world.Blocks)
    {
      if (
        block?.Code == null
        || block is not BlockLiquidContainerBase cont
        || !block.Code.Path.Contains("woodbucket")
      )
        continue;
      var bucket = new ItemStack(block);
      cont.SetContent(bucket, waterStack);
      list.Add(bucket);
    }
    // Fall back to a bare water portion if no fillable bucket resolved.
    if (list.Count == 0 && waterStack.Collectible != null)
      list.Add(waterStack);

    return _waterContainerStacks = list.ToArray();
  }

  #endregion
}

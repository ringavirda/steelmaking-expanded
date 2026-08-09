using System.Collections.Generic;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Boiler;

/// <summary>
/// Shared base for the boiler mega-blocks. Each occupies one grid cell but renders across a
/// multi-cell volume reserved with invisible structure fillers (so the player gets real
/// collision); construction is driven by the RightClickConstructable behavior in the block JSON.
/// </summary>
public abstract class BlockBoiler
  : Block,
    INetworkConnector,
    IFillerInteractionTarget {
  // The body extends along local +z; offset the angle 180° so HorizontalOrientable raises it
  // AWAY from the player. rotateYByType is offset to match, keeping visual/fillers/connectors aligned.
  private int Angle =>
    (ExOrientation.AngleFromSide(Variant["side"]) + 180) % 360;

  /// <summary>The structure/filler rotation angle, for multiblockStructure verification.</summary>
  public int StructureAngle => Angle;

  // Water draws through a pipe on the bottom face; steam and exhaust leave via their outlet cells.
  public string NetworkType => "pipe";

  public bool HasConnectorAt(BlockFacing face) => face == BlockFacing.DOWN;

  /// <summary>The concrete boiler's generated offset accessors (Lancashire/Cornish implement it).</summary>
  private IBoilerGeometry Geo => (IBoilerGeometry)this;

  private BlockPos OffsetWorldPos(
    BlockPos boilerPos,
    JsonObject? offsetNode,
    Vec3i fallback
  ) => ExOrientation.WorldPosFromAttr(boilerPos, offsetNode, fallback, Angle);

  /// <summary>World cell of the firebox slot.</summary>
  public BlockPos FuelWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, Geo.FuelOffset, new Vec3i(0, 0, -1));

  /// <summary>World cell of the exhaust gas outlet.</summary>
  public BlockPos ExhaustOutletWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, Geo.ExhaustOutletOffset, new Vec3i(0, 1, 4));

  /// <summary>World cell of the filler that carries the access lid.</summary>
  public BlockPos LidWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, Geo.LidOffset, new Vec3i(0, 1, 0));

  /// <summary>
  /// World cell of the steam connector (the port filler atop the body); the steam pipe attaches
  /// in the cell directly above it.
  /// </summary>
  public BlockPos SteamPipeWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, Geo.SteamConnectorOffset, new Vec3i(0, 1, 2));

  /// <summary>
  /// World cell the animated vessel mesh is lit from. Vanilla lights the whole footprint from
  /// the block's firebox-adjacent cell, tinting the vessel red at night; this points the light
  /// sample at a body cell instead. Read from <c>lightSampleOffset</c>, rotated by angle.
  /// </summary>
  public BlockPos LightSampleWorldPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, Geo.LightSampleOffset, new Vec3i(0, 1, 2));

  /// <summary>
  /// World cell at the footprint centre, where the burst explosion is centred so it goes off
  /// inside the boiler. Read from <c>explosionCenterOffset</c>, rotated by angle.
  /// </summary>
  public BlockPos ExplosionCenterPos(BlockPos boilerPos) =>
    OffsetWorldPos(boilerPos, Geo.ExplosionCenterOffset, new Vec3i(0, 1, 1));

  /// <summary>Removes the boiler's reserved filler footprint (used by the explosion path).</summary>
  public void RemoveStructure(IWorldAccessor world, BlockPos pos) =>
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells((IFillerHost)this, pos, Angle)
    );

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    // Refuse placement unless the whole volume is clear, else the fillers fail to spawn.
    var cells = StructureFillers.FootprintCells(
      (IFillerHost)this,
      blockSel.Position,
      Angle
    );
    if (!StructureFillers.CanPlace(world, cells)) {
      failureCode = "notenoughspace";
      return false;
    }
    return true;
  }

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos blockPos,
    ItemStack? byItemStack = null
  ) {
    base.OnBlockPlaced(world, blockPos, byItemStack);
    StructureFillers.PlaceFillers(
      world,
      blockPos,
      StructureFillers.FootprintCells((IFillerHost)this, blockPos, Angle)
    );
    MarkSteamPort(world, blockPos);
  }

  /// <summary>
  /// Turns the steam-connector filler cell (<see cref="SteamPipeWorldPos"/>) into an upward "pipe"
  /// port, so a steam pipe placed above it connects straight into the boiler.
  /// </summary>
  private void MarkSteamPort(IWorldAccessor world, BlockPos boilerPos) {
    if (world.Side != EnumAppSide.Server)
      return;
    BlockPos portCell = SteamPipeWorldPos(boilerPos);
    if (
      world.BlockAccessor.GetBlockEntity(portCell)
      is BlockEntityStructureFiller be
    ) {
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
  ) {
    // Clear the reserved volume first so no invisible solid cells are left behind.
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells((IFillerHost)this, pos, Angle)
    );

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  // A broken boiler returns its own crafted frame alongside the construction materials the
  // RightClickConstructable behaviour scatters, the same way the engines do. The boiler is placed
  // from a crafted item, so withholding that item made breaking one a net loss.

  #region Lid interactions

  // The lid and manual fill live on one footprint cell (LidWorldPos). Interactions arrive on
  // the boiler's own cell or forwarded from a filler; both funnel into the Handle* helpers,
  // which gate on the clicked cell being the lid cell and otherwise defer to the default
  // behavior (construction, structure projection).

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
  /// Shared lid/fill click logic. <paramref name="sel"/> is the boiler's own cell (for BE lookup);
  /// <paramref name="clickedCell"/> is the cell looked at. Returns <c>null</c> to defer.
  /// </summary>
  private bool? HandleInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel,
    BlockPos clickedCell
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
      is not BlockEntityBoiler be
    )
      return null;

    // Ctrl+Shift is the structure-projection gesture; pre-construction clicks drive RCC.
    if (byPlayer.Entity.Controls.CtrlKey && byPlayer.Entity.Controls.ShiftKey)
      return null;
    if (!be.IsConstructed)
      return null;

    // The lid and manual fill only respond on the lid-bearing cell.
    if (!clickedCell.Equals(LidWorldPos(sel.Position)))
      return null;

    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;

    // A water container while the lid is open → pour its entire contents in.
    if (be.LidOpen && IsWaterContainer(slot?.Itemstack)) {
      if (world.Side == EnumAppSide.Server && slot != null)
        be.TryManualFill(byPlayer, slot);
      return true;
    }

    // An empty liquid container while the lid is open → bail water out into it.
    if (be.LidOpen && IsEmptyLiquidContainer(slot?.Itemstack)) {
      if (world.Side == EnumAppSide.Server && slot != null)
        be.TryManualDrain(byPlayer, slot);
      return true;
    }

    // Empty hands → begin the lid hold; the toggle happens in the step loop past the threshold.
    if (slot?.Empty != false) {
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
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
        is not BlockEntityBoiler be
      || !be.IsConstructed
      || !clickedCell.Equals(LidWorldPos(sel.Position))
    )
      return null;

    // Only the empty-handed hold uses the step loop; a held item ends the interaction at once.
    if (byPlayer.InventoryManager?.ActiveHotbarSlot?.Empty != true)
      return false;

    // Toggle once past the threshold, then keep returning true until release so the engine
    // doesn't restart the interaction (which would toggle the lid repeatedly).
    if (secondsUsed >= LidHoldSeconds && !be.LidToggled) {
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
  ) {
    if (!HandleInteractStop(world, byPlayer, blockSel))
      base.OnBlockInteractStop(secondsUsed, world, byPlayer, blockSel);
  }

  void IFillerInteractionTarget.OnFillerInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) {
    if (!HandleInteractStop(world, byPlayer, principalSel))
      base.OnBlockInteractStop(secondsUsed, world, byPlayer, principalSel);
  }

  private bool HandleInteractStop(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection sel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(sel.Position)
        is not BlockEntityBoiler be
      || !be.IsConstructed
    )
      return false;

    be.LidToggled = false;
    return true;
  }

  private static bool IsWaterContainer(ItemStack? stack) {
    if (stack?.Collectible is not BlockLiquidContainerBase cont)
      return false;
    ItemStack? content = cont.GetContent(stack);
    return content?.Collectible?.Code?.Path?.Contains("water") == true;
  }

  /// <summary>An empty liquid container (e.g. an empty bucket) - the tool used to bail water out.</summary>
  private static bool IsEmptyLiquidContainer(ItemStack? stack) {
    if (stack?.Collectible is not BlockLiquidContainerBase cont)
      return false;
    return cont.GetContent(stack) == null;
  }

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    var help = HandleInteractionHelp(
      world,
      selection,
      forPlayer,
      selection.Position
    );
#if !GAME_GE_1_22
    // Legacy lacks the vanilla IInteractableWithHelp path, so surface the construction help here.
    help =
      ExpandedLib.Blocks.Construction.ExRightClickConstructable.AppendConstructionHelp(
        world,
        selection,
        help
      );
#endif
    return help;
  }

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
  ) {
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
      new WorldInteraction {
        ActionLangCode = "ppex:blockhelp-boiler-lid",
        MouseButton = EnumMouseButton.Right,
        RequireFreeHand = true,
      }
    );

    // The manual water fill/drain only work while the lid is open, so only advertise them then.
    if (be.LidOpen) {
      help.Add(
        new WorldInteraction {
          ActionLangCode = "ppex:blockhelp-boiler-fill",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = WaterContainerStacks(world),
        }
      );
      help.Add(
        new WorldInteraction {
          ActionLangCode = "ppex:blockhelp-boiler-drain",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = EmptyContainerStacks(world),
        }
      );
    }

    return help.ToArray();
  }

  /// <summary>Water-filled liquid containers shown on the manual-fill interaction hint, resolved once.</summary>
  private static ItemStack[]? _waterContainerStacks;

  private static ItemStack[] WaterContainerStacks(IWorldAccessor world) {
    if (_waterContainerStacks != null)
      return _waterContainerStacks;

    var waterStack = new ItemStack(
      world.GetItem(new AssetLocation("game:waterportion"))
    );
    var list = new List<ItemStack>();
    foreach (var block in world.Blocks) {
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

  /// <summary>Empty liquid containers shown on the manual-drain interaction hint, resolved once.</summary>
  private static ItemStack[]? _emptyContainerStacks;

  private static ItemStack[] EmptyContainerStacks(IWorldAccessor world) {
    if (_emptyContainerStacks != null)
      return _emptyContainerStacks;

    var list = new List<ItemStack>();
    foreach (var block in world.Blocks) {
      if (
        block?.Code == null
        || block is not BlockLiquidContainerBase
        || !block.Code.Path.Contains("woodbucket")
      )
        continue;
      list.Add(new ItemStack(block));
    }

    return _emptyContainerStacks = list.ToArray();
  }

  #endregion
}

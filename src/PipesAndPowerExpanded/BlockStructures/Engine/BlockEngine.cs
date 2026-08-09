using System;
using System.Linq;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

/// <summary>
/// Shared base for the steam engine mega-blocks. Each occupies one grid cell but renders
/// across a footprint reserved with invisible structure fillers; construction is driven by
/// the RightClickConstructable behavior in the block JSON. In north orientation it exposes
/// pipe connectors on south (steam intake) and east (condensed water out), both rotating
/// with the block.
/// </summary>
public abstract class BlockEngine
  : Block,
    INetworkConnector,
    IFillerInteractionTarget {
  // Pipe ports in north orientation, rotated to the placed orientation at runtime.
  private static readonly BlockFacing[] BaseConnectorFaces =
  [
    BlockFacing.SOUTH, // steam intake
    BlockFacing.EAST, // condensed water out
  ];

  // Raw side angle (north 0, west 90, south 180, east 270).
  protected int Angle => ExOrientation.AngleFromSide(Variant["side"]) % 360;

  // The body extends along local +z, but rotateYByType raises the mesh 180° from that so the
  // body points AWAY from the player. Everything lining up with the visible body (fillers,
  // connectors, sub-machine, gear housing) lives in this +180 "body" frame.
  protected int BodyAngle => (Angle + 180) % 360;

  public string NetworkType => "pipe";

  public bool HasConnectorAt(BlockFacing face) {
    foreach (var baseFace in BaseConnectorFaces) {
      if (ExOrientation.RotateFacing(baseFace, Angle) == face)
        return true;
    }
    return false;
  }

  /// <summary>The rotated facing the steam intake sits on (local-south).</summary>
  public BlockFacing SteamInletFace =>
    ExOrientation.RotateFacing(BlockFacing.SOUTH, Angle);

  /// <summary>The rotated facing the condensed water exits through (local-east).</summary>
  public BlockFacing WaterOutletFace =>
    ExOrientation.RotateFacing(BlockFacing.EAST, Angle);

  /// <summary>Default sub-machine cell (local {0,0,2}) when <c>submachineOffset</c> is unset.</summary>
  private static readonly Vec3i DefaultSubmachineOffset = new(0, 0, 2);

  /// <summary>Default gear-housing cell (local {0,3,1}) when <c>gearHousingOffset</c> is unset.</summary>
  private static readonly Vec3i DefaultGearHousingOffset = new(0, 3, 1);

  /// <summary>
  /// World cell of the attached sub-machine, read from the <c>submachineOffset</c> JSON attribute
  /// in the engine's visual-front frame (inverted by the sub-machine's back-reference).
  /// </summary>
  public BlockPos SubmachinePos(BlockPos enginePos) =>
    ExOrientation.WorldPosFromAttr(
      enginePos,
      ((IEngineGeometry)this).SubmachineOffset,
      DefaultSubmachineOffset,
      BodyAngle
    );

  /// <summary>
  /// Compass-clockwise mapping from an engine's facing to its sub-machine's facing: north→east,
  /// east→south, south→west, west→north. The single rule both placement directions use.
  /// </summary>
  public static string SubmachineSide(string engineSide) =>
    engineSide switch {
      "north" => "east",
      "east" => "south",
      "south" => "west",
      "west" => "north",
      _ => "north",
    };

  /// <summary>
  /// Locates the engine owning the sub-machine cell at <paramref name="submachinePos"/>. The engine
  /// sits two cells away horizontally, so test the four candidates and confirm each engine's
  /// <see cref="SubmachinePos"/> points back here - works for any orientation and offset.
  /// </summary>
  public static bool TryFindEngineFor(
    IBlockAccessor blockAccessor,
    BlockPos submachinePos,
    out BlockPos enginePos,
    out BlockEngine engineBlock
  ) {
    foreach (var f in BlockFacing.HORIZONTALS) {
      BlockPos cand = submachinePos.AddCopy(
        f.Normali.X * 2,
        0,
        f.Normali.Z * 2
      );
      if (
        blockAccessor.GetBlock(cand) is BlockEngine eng
        && eng.SubmachinePos(cand).Equals(submachinePos)
      ) {
        enginePos = cand;
        engineBlock = eng;
        return true;
      }
    }
    enginePos = null!;
    engineBlock = null!;
    return false;
  }

  /// <summary>
  /// World cell of the gear housing atop the engine (<c>gearHousingOffset</c> JSON attribute) where
  /// the running machine emits its gear hum. Same visual-front frame as the sub-machine.
  /// </summary>
  public BlockPos GearHousingPos(BlockPos enginePos) =>
    ExOrientation.WorldPosFromAttr(
      enginePos,
      ((IEngineGeometry)this).GearHousingOffset,
      DefaultGearHousingOffset,
      BodyAngle
    );

  /// <summary>Default cylinder-vent point (master-cell frame): the top of the piston cylinder,
  /// on the engine's centre line a few cells up. Both stock engines share this piston layout.</summary>
  private static readonly Vec3d DefaultCylinderVent = new(0.5, 1.5, 0.5);

  /// <summary>
  /// World point at the top of the piston cylinder, where it puffs spent steam while running (and
  /// hard while over-pressure). Read from the optional <c>cylinderVentOffset</c> JSON attribute
  /// (master-cell frame); the horizontal part rotates by the body angle to track the cylinder.
  /// </summary>
  public Vec3d CylinderVentPos(BlockPos enginePos) {
    Vec3d off = ReadVentOffset();
    float x = (float)off.X;
    float z = (float)off.Z;
    ExOrientation.RotateAroundCenter(ref x, ref z, BodyAngle);
    return new Vec3d(enginePos.X + x, enginePos.Y + off.Y, enginePos.Z + z);
  }

  // No engine JSON declares cylinderVentOffset (so there's no generated member); it's the coded
  // default. CylinderVentPos still rotates the horizontal part by the body angle.
  private Vec3d ReadVentOffset() => DefaultCylinderVent;

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  ) {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    var cells = StructureFillers.FootprintCells(
      (IFillerHost)this,
      blockSel.Position,
      BodyAngle
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
      StructureFillers.FootprintCells((IFillerHost)this, blockPos, BodyAngle)
    );

    // Snap an already-present sub-machine (built before the engine) to the matching facing.
    if (world.Side == EnumAppSide.Server)
      ReorientSubmachine(world, blockPos);
  }

  /// <summary>
  /// Snaps a sub-machine at this engine's cell to the matching orientation (see
  /// <see cref="SubmachineSide"/>). <c>ExchangeBlock</c> keeps the block entity alive, re-binding
  /// its animator and engine back-reference via <c>OnExchanged</c>.
  /// </summary>
  private void ReorientSubmachine(IWorldAccessor world, BlockPos enginePos) {
    var ba = world.BlockAccessor;
    BlockPos subPos = SubmachinePos(enginePos);
    if (ba.GetBlock(subPos) is not BlockEngineSubmachine sub)
      return;
    string want = SubmachineSide(Variant["side"]);
    if (sub.Variant["side"] == want)
      return;
    Block? target = world.GetBlock(sub.CodeWithVariant("side", want));
    if (target == null)
      return;
    ba.ExchangeBlock(target.BlockId, subPos);
    ba.MarkBlockDirty(subPos);
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells((IFillerHost)this, pos, BodyAngle)
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

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    var help = RepairInteractionHelp(
      world,
      selection.Position,
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
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

  /// <summary>
  /// Appends the wrench-repair action to <paramref name="baseHelp"/> when the engine is broken.
  /// Shared by the engine cell and footprint fillers so the hint shows wherever the player looks.
  /// </summary>
  private WorldInteraction[] RepairInteractionHelp(
    IWorldAccessor world,
    BlockPos enginePos,
    WorldInteraction[]? baseHelp
  ) {
    baseHelp ??= [];

    // Only a broken engine is repairable - show the wrench action then.
    if (
      world.BlockAccessor.GetBlockEntity(enginePos) is not BlockEntityEngine be
      || !be.IsBroken
    )
      return baseHelp;

    var repairHelp = new WorldInteraction {
      ActionLangCode = "ppex:blockhelp-engine-repair",
      MouseButton = EnumMouseButton.Right,
      Itemstacks = ExItems.WrenchStacks(world),
    };
    return [.. baseHelp, repairHelp];
  }

  #region IFillerInteractionTarget

  // Footprint fillers forward player interaction here; by default a filler cell behaves like
  // clicking the engine itself. Cornish overrides these for its per-cell control rods.
  // Forwarding uses the vanilla base help (not the virtual GetPlacedBlockInteractionHelp) so a
  // subclass's per-cell extras aren't shown on every footprint cell.

  public virtual bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) => OnBlockInteractStart(world, byPlayer, principalSel);

  public bool OnFillerInteractStep(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) => OnBlockInteractStep(secondsUsed, world, byPlayer, principalSel);

  public void OnFillerInteractStop(
    float secondsUsed,
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) => OnBlockInteractStop(secondsUsed, world, byPlayer, principalSel);

  public virtual WorldInteraction[] GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  ) =>
    RepairInteractionHelp(
      world,
      principalSel.Position,
      base.GetPlacedBlockInteractionHelp(world, principalSel, forPlayer)
    );

  #endregion

  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    // A held placeable block (not a liquid container) = the player is building against the
    // engine (e.g. a pipe on the steam inlet/water outlet); let vanilla place it on the clicked
    // face. Construction materials and the wrench are items, so they fall through below.
    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    if (held?.Block != null && held.Collectible is not BlockLiquidContainerBase)
      return false;

    // A broken engine only responds to a wrench repair until it is fixed.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityEngine be
      && be.IsBroken
    ) {
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
    BlockEntityEngine be
  ) {
    var player = byPlayer as IServerPlayer;
    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;

    if (slot?.Itemstack?.Collectible?.Code?.Path?.Contains("wrench") != true) {
      player?.SendIngameError(
        "ppex-engine",
        Lang.Get("ppex:engine-repair-wrench")
      );
      PrintRepairMaterials(player);
      return;
    }

    // Creative players repair instantly with the wrench - no materials needed or consumed.
    bool creative =
      byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
    if (!creative) {
      bool hasAll = RepairItems.All(r =>
        ExInventory.Count(byPlayer, stack => Matches(stack, r.Codes))
        >= r.Quantity
      );
      if (!hasAll) {
        // Print exactly what the repair needs to the chat instead of cluttering the help.
        PrintRepairMaterials(player);
        return;
      }

      foreach (var r in RepairItems)
        ExInventory.Take(
          byPlayer,
          stack => Matches(stack, r.Codes),
          r.Quantity
        );
    }

    be.Repair();
    ExSounds.PlayAt(world, be.Pos, ExSounds.MePostHit, byPlayer);
    player?.SendMessage(
      GlobalConstants.CurrentChatGroup,
      Lang.Get("ppex:engine-repaired"),
      EnumChatType.Notification
    );
  }

  /// <summary>Prints the wrench + material requirements for a repair to the player's chat.</summary>
  private void PrintRepairMaterials(IServerPlayer? player) =>
    player?.SendMessage(
      GlobalConstants.CurrentChatGroup,
      Lang.Get("ppex:engine-repair-materials", RepairDescription),
      EnumChatType.Notification
    );

  private static bool Matches(ItemStack stack, string[] codes) =>
    stack.Collectible?.Code != null
    && codes.Contains(stack.Collectible.Code.Path);

  #endregion
}

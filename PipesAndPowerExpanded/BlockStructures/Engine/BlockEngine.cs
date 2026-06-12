using System;
using System.Linq;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using PipesAndPowerExpanded.BlockStructures.Engine.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace PipesAndPowerExpanded.BlockStructures.Engine;

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
public abstract class BlockEngine
  : Block,
    INetworkConnector,
    IFillerInteractionTarget
{
  // Pipe ports in north orientation, rotated to the placed orientation at runtime.
  private static readonly BlockFacing[] BaseConnectorFaces =
  [
    BlockFacing.SOUTH, // steam intake
    BlockFacing.EAST, // condensed water out
  ];

  // Raw side angle (north 0, west 90, south 180, east 270).
  protected int Angle => ExOrientation.AngleFromSide(Variant["side"]) % 360;

  // The structure body extends along the local +z ("south") axis, but the JSON
  // rotateYByType raises the mesh 180° from that so HorizontalOrientable points the body
  // AWAY from the player. Everything that has to line up with the visible body — fillers,
  // pipe connectors, sub-machine and gear housing — lives in this same +180 "body" frame.
  protected int BodyAngle => (Angle + 180) % 360;

  public string NetworkType => "pipe";

  public bool HasConnectorAt(BlockFacing face)
  {
    foreach (var baseFace in BaseConnectorFaces)
    {
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
  /// World cell of the attached sub-machine, read from the <c>submachineOffset</c> JSON
  /// attribute and placed in the engine's visual-front frame. The same offset is inverted by
  /// the sub-machine's back-reference in <c>BlockEntityEngineSubmachine.FindEngine</c>.
  /// </summary>
  public BlockPos SubmachinePos(BlockPos enginePos) =>
    ExOrientation.WorldPosFromAttr(
      this,
      enginePos,
      "submachineOffset",
      DefaultSubmachineOffset,
      BodyAngle
    );

  /// <summary>
  /// Compass-clockwise mapping from an engine's facing to the facing its sub-machine must take
  /// to line up with the engine body: north→east, east→south, south→west, west→north. (An engine
  /// facing north drives a sub-machine facing east.) This is the single rule both placement
  /// directions use to keep the sub-machine cell in its one valid orientation.
  /// </summary>
  public static string SubmachineSide(string engineSide) =>
    engineSide switch
    {
      "north" => "east",
      "east" => "south",
      "south" => "west",
      "west" => "north",
      _ => "north",
    };

  /// <summary>
  /// Locates the engine that owns the sub-machine cell at <paramref name="submachinePos"/>, if
  /// any. The engine sits two cells away along a horizontal axis (the (0,0,2) sub-machine offset
  /// rotated into one of the four orientations), so we test those four candidates and confirm
  /// each engine's own <see cref="SubmachinePos"/> points back at this cell — handling any
  /// orientation (and a non-default offset) without assuming which way the engine faces.
  /// </summary>
  public static bool TryFindEngineFor(
    IBlockAccessor blockAccessor,
    BlockPos submachinePos,
    out BlockPos enginePos,
    out BlockEngine engineBlock
  )
  {
    foreach (var f in BlockFacing.HORIZONTALS)
    {
      BlockPos cand = submachinePos.AddCopy(
        f.Normali.X * 2,
        0,
        f.Normali.Z * 2
      );
      if (
        blockAccessor.GetBlock(cand) is BlockEngine eng
        && eng.SubmachinePos(cand).Equals(submachinePos)
      )
      {
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
  /// World cell of the gear housing atop the engine, read from the <c>gearHousingOffset</c>
  /// JSON attribute — where the running machine emits its constant low planetary-gear hum.
  /// Uses the same visual-front frame as the sub-machine so the sound sits on the rendered
  /// front of the engine rather than behind it.
  /// </summary>
  public BlockPos GearHousingPos(BlockPos enginePos) =>
    ExOrientation.WorldPosFromAttr(
      this,
      enginePos,
      "gearHousingOffset",
      DefaultGearHousingOffset,
      BodyAngle
    );

  /// <summary>Default cylinder-vent point (master-cell frame): the top of the piston cylinder,
  /// on the engine's centre line a few cells up. Both stock engines share this piston layout.</summary>
  private static readonly Vec3d DefaultCylinderVent = new(0.5, 1.5, 0.5);

  /// <summary>
  /// World point at the top of the engine's piston cylinder, where it puffs spent steam while
  /// running (and vents hard while over-pressure). Read from the optional
  /// <c>cylinderVentOffset</c> JSON attribute (block-unit doubles in the master-cell frame); the
  /// horizontal part is rotated by the visual body angle so it tracks the rendered cylinder. The
  /// cylinder sits on the engine's centre line, so for the stock models this is the cell centre.
  /// </summary>
  public Vec3d CylinderVentPos(BlockPos enginePos)
  {
    Vec3d off = ReadVentOffset();
    float x = (float)off.X;
    float z = (float)off.Z;
    ExOrientation.RotateAroundCenter(ref x, ref z, BodyAngle);
    return new Vec3d(enginePos.X + x, enginePos.Y + off.Y, enginePos.Z + z);
  }

  private Vec3d ReadVentOffset() =>
    ExOrientation.ReadOffsetD(this, "cylinderVentOffset", DefaultCylinderVent);

  public override bool CanPlaceBlock(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    ref string failureCode
  )
  {
    if (!base.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
      return false;

    var cells = StructureFillers.FootprintCells(
      this,
      blockSel.Position,
      BodyAngle
    );
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
      StructureFillers.FootprintCells(this, blockPos, BodyAngle)
    );

    // A sub-machine built before its engine still ends up correctly oriented under it: snap an
    // already-present sub-machine at our sub-machine cell to the matching facing.
    if (world.Side == EnumAppSide.Server)
      ReorientSubmachine(world, blockPos);
  }

  /// <summary>
  /// Snaps a sub-machine already sitting at this engine's sub-machine cell to the orientation
  /// that lines up with the engine (see <see cref="SubmachineSide"/>). The swap keeps the
  /// sub-machine's block entity alive (<c>ExchangeBlock</c>), which re-binds its animator and
  /// re-resolves its engine back-reference via <c>OnExchanged</c>.
  /// </summary>
  private void ReorientSubmachine(IWorldAccessor world, BlockPos enginePos)
  {
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
  )
  {
    StructureFillers.RemoveFillers(
      world,
      pos,
      StructureFillers.FootprintCells(this, pos, BodyAngle)
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
  ) =>
    RepairInteractionHelp(
      world,
      selection.Position,
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
    );

  /// <summary>
  /// Appends the wrench-repair action to <paramref name="baseHelp"/> when the engine at
  /// <paramref name="enginePos"/> is broken (the materials it needs are printed to chat
  /// on interaction). Shared by the engine cell and the footprint fillers so the repair
  /// hint shows wherever the player looks at the burst engine.
  /// </summary>
  private WorldInteraction[] RepairInteractionHelp(
    IWorldAccessor world,
    BlockPos enginePos,
    WorldInteraction[]? baseHelp
  )
  {
    baseHelp ??= [];

    // Only a broken engine is repairable — show the wrench action then.
    if (
      world.BlockAccessor.GetBlockEntity(enginePos) is not BlockEntityEngine be
      || !be.IsBroken
    )
      return baseHelp;

    var repairHelp = new WorldInteraction
    {
      ActionLangCode = "ppex:blockhelp-engine-repair",
      MouseButton = EnumMouseButton.Right,
      Itemstacks = ExItems.WrenchStacks(world),
    };
    return [.. baseHelp, repairHelp];
  }

  #region IFillerInteractionTarget

  // The engine renders across a footprint reserved with invisible structure fillers, which
  // forward player interaction to the engine here. By default a filler cell behaves exactly
  // like clicking the engine itself (repair, the held-block build passthrough). Cornish
  // overrides these to add its per-cell steam-throttle control rods, which only respond on
  // the engine cell and the filler directly above it. Forwarding goes through the vanilla
  // base help (not the virtual GetPlacedBlockInteractionHelp) so a subclass's per-cell extras
  // aren't shown on every footprint cell.

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
  )
  {
    // A held placeable block (not a liquid container) means the player wants to build
    // against the engine — e.g. attach a pipe to the steam inlet / water outlet, which sit
    // on the engine's own exposed faces. Don't swallow the click for the RCC/BlockEntityInteract
    // behaviors; let vanilla place the block on the clicked face. Construction materials and the
    // repair wrench are items (no Block), so they still fall through to the handling below.
    // Mirrors BlockStructureFiller's placeable-block passthrough.
    ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
    if (held?.Block != null && held.Collectible is not BlockLiquidContainerBase)
      return false;

    // A broken engine only responds to a wrench repair until it is fixed.
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is BlockEntityEngine be
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
    BlockEntityEngine be
  )
  {
    var player = byPlayer as IServerPlayer;
    ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;

    if (slot?.Itemstack?.Collectible?.Code?.Path?.Contains("wrench") != true)
    {
      player?.SendIngameError(
        "ppex-engine",
        Lang.Get("ppex:engine-repair-wrench")
      );
      PrintRepairMaterials(player);
      return;
    }

    // Creative players repair instantly with the wrench — no materials needed or consumed.
    bool creative =
      byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
    if (!creative)
    {
      bool hasAll = RepairItems.All(r =>
        ExInventory.Count(byPlayer, stack => Matches(stack, r.Codes))
        >= r.Quantity
      );
      if (!hasAll)
      {
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

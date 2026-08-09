using System;
using System.Collections.Generic;
using ExpandedLib.Blocks.Structures;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockStructures.Converter.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SteelmakingExpanded.BlockStructures.Converter.Blocks;

/// <summary>
/// The 3×3×3 converter vessel block. Construction is driven by the
/// RightClickConstructable block-entity behavior; this block scatters any
/// solidified charge when broken with an iron-tier pickaxe.
/// <para>
/// Implements <see cref="IFillerInteractionTarget"/> so a small hardened residue (one that
/// solidified mid-pour) can be chiselled out of the upper-rear <c>(0,1,1)</c> footprint cell with a
/// chisel + hammer, rather than forcing the player to break the whole vessel.
/// </para>
/// </summary>
[BlockRegister]
public partial class BlockConverterBessemer
  : Block,
    IFillerHost,
    IFillerInteractionTarget {
  // RMB construction and its build prompts are routed to the
  // RightClickConstructable block-entity behaviour by the "BlockEntityInteract"
  // block behaviour declared in the block JSON.

  // The converter is welded plate over a refractory lining - breaking it needs an iron-tier pickaxe.
  // The actual mining requirement is enforced via "requiredMiningTier" in the
  // block JSON; here we just scatter whatever solidified charge it held.
  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    ItemStack? solidifiedDrops = null;
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityConverterBessemer be
    )
      solidifiedDrops = be.CollectBreakDrops();

    // Clear the reserved 3x3x3 filler volume. Done before base.OnBlockBroken so
    // that even if the construction-drop path below throws, the fillers are gone
    // and we don't leave invisible solid cells behind.
    int fillerAngle = ExOrientation.AngleFromSide(Variant["side"]);
    var fillerCells = StructureFillers.FootprintCells(this, pos, fillerAngle);
    StructureFillers.RemoveFillers(world, pos, fillerCells);

    // base.OnBlockBroken drives the RightClickConstructable behaviour, which
    // resolves each completed stage's ingredients back into drops. That path
    // expands wildcard codes (e.g. metalplate-*) using the wildcard values
    // captured at build time; a converter raised before "storeWildCard" was
    // added to the recipe has none stored, so vanilla GetDrops throws while
    // expanding the "*" - and because it runs before the block is cleared, the
    // exception escapes to the client and crashes the game. Guard it so a
    // legacy/corrupt construction state degrades to "no construction drops"
    // and the block is still removed.
    try {
      base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
    } catch (Exception e) {
      world.Logger.Warning(
        "[smex] Bessemer converter at {0} could not drop its construction "
          + "materials (likely built before the recipe fix); removing it "
          + "anyway. {1}",
        pos,
        e
      );
      if (world.BlockAccessor.GetBlock(pos) == this)
        world.BlockAccessor.SetBlock(0, pos);
    }

    if (solidifiedDrops != null && world.Side == EnumAppSide.Server)
      world.SpawnItemEntity(solidifiedDrops, pos.ToVec3d().Add(0.5, 0.5, 0.5));
  }

  // The vessel is spawned by the control block, never placed from an item, so there is no vessel item
  // to hand back: dropping itself would mint a block the player could not have crafted. What it does
  // return is the placement cost the control block took from the player's hotbar - the large gear and
  // the rods - on top of the construction materials the RightClickConstructable behaviour scatters and
  // the solidified charge OnBlockBroken spawns. The JSON "drops": [] suppresses the self-drop, but it
  // is not always honoured for a variant block (the per-side variant can still be handed its own code
  // as a fallback drop at registration), so the override is what actually enforces it.
  //
  // The gear and rods come back in iron: either tier is accepted at placement and which one was
  // consumed is not recorded, so the refund is the cheaper of the two.
  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    var drops = new List<ItemStack>(2);

    if (
      world.GetItem(new AssetLocation("ppex:largegear-iron")) is { } gear
      && SmexValues.BessemerRequiredGears > 0
    )
      drops.Add(new ItemStack(gear, SmexValues.BessemerRequiredGears));

    if (
      world.GetItem(new AssetLocation("game:rod-iron")) is { } rod
      && SmexValues.BessemerRequiredRods > 0
    )
      drops.Add(new ItemStack(rod, SmexValues.BessemerRequiredRods));

    return [.. drops];
  }

  #region Chisel-out interaction (IFillerInteractionTarget)

  // Every footprint cell forwards interaction to this principal block. We single out the (0,1,1)
  // hatch cell for the chisel-out and forward everything else to the normal construction handling.
  public bool OnFillerInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection principalSel,
    BlockPos clickedCell
  ) {
    if (IsChiselCell(principalSel.Position, clickedCell)) {
      if (TryChargeScrap(world, byPlayer, principalSel.Position))
        return true;
      if (TryChiselOut(world, byPlayer, principalSel.Position))
        return true;
    }
    return OnBlockInteractStart(world, byPlayer, principalSel);
  }

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

  public WorldInteraction[] GetFillerInteractionHelp(
    IWorldAccessor world,
    BlockSelection principalSel,
    IPlayer forPlayer,
    BlockPos clickedCell
  ) {
    WorldInteraction[] baseHelp =
      GetPlacedBlockInteractionHelp(world, principalSel, forPlayer) ?? [];

    if (!IsChiselCell(principalSel.Position, clickedCell))
      return baseHelp;

    var hints = new List<WorldInteraction>(baseHelp)
    {
      new()
      {
        ActionLangCode = "smex:blockhelp-bessemer-scrap",
        MouseButton = EnumMouseButton.Right,
        Itemstacks = ScrapStacks(world),
      },
    };

    // The chisel hint shows only once the residue is small and hardened.
    if (
      world.BlockAccessor.GetBlockEntity(principalSel.Position)
        is BlockEntityConverterBessemer be
      && be.CanChiselOut()
    )
      hints.Add(
        MoltenChisel.ChiselHelp(world, "smex:blockhelp-bessemer-chiselresidue")
      );

    return [.. hints];
  }

  private bool IsChiselCell(BlockPos principalPos, BlockPos clickedCell) {
    BlockPos chiselCell = ExOrientation.WorldPosFromAttr(
      principalPos,
      ChiselOffset,
      new Vec3i(),
      ExOrientation.AngleFromSide(Variant["side"])
    );
    return clickedCell.Equals(chiselCell);
  }

  // The scrap items the hatch advertises, resolved once from the configured code list.
  private ItemStack[]? _scrapStacks;

  private ItemStack[] ScrapStacks(IWorldAccessor world) {
    if (_scrapStacks != null)
      return _scrapStacks;
    var stacks = new List<ItemStack>();
    foreach (
      string code in SmexValues.BessemerScrapCodes.Split(
        ',',
        StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
      )
    )
      if (world.GetItem(new AssetLocation(code)) is { } item)
        stacks.Add(new ItemStack(item));
    return _scrapStacks = [.. stacks];
  }

  // Scrap in hand charges it into the vessel through the same hatch the chisel reaches. Returns true
  // when this owns the click, so it isn't forwarded to construction handling.
  private static bool TryChargeScrap(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos principalPos
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(principalPos)
      is not BlockEntityConverterBessemer be
    )
      return false;

    if (!be.TryChargeScrap(byPlayer, out string error))
      return false;

    if (world.Side == EnumAppSide.Client && error.Length > 0)
      (byPlayer as IClientPlayer)?.ShowChatNotification(error);
    return true;
  }

  // Chisel in hand + hammer in the off-hand chips a hardened residue out of the vessel via the shared
  // MoltenChisel ritual. Returns true when this owns the click (chiselled, or claimed-but-not-ready with
  // a "too hot"/"too full" message), so it isn't forwarded to construction handling.
  private bool TryChiselOut(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockPos principalPos
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(principalPos)
      is not BlockEntityConverterBessemer be
    )
      return false;

    return MoltenChisel.TryChisel(
        world,
        byPlayer,
        principalPos,
        be,
        ExSounds.StoneCrush
      ) != ChiselOutcome.NotChiseling;
  }

  #endregion

#if !GAME_GE_1_22
  // Legacy lacks the vanilla IInteractableWithHelp path, so surface the construction help here.
  public override Vintagestory.API.Client.WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) =>
    ExpandedLib.Blocks.Construction.ExRightClickConstructable.AppendConstructionHelp(
      world,
      selection,
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer)
    );
#endif
}

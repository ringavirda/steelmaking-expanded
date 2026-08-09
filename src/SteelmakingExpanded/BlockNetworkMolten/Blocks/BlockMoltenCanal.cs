using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Blocks.Networks;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.BlockNetworkMolten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockNetworkMolten.Blocks;

/// <summary>
/// The base molten-canal block: a self-orienting node of the "molten" network that
/// carries liquid metal. Provides the orientation tables shared by every
/// straight/bend/junction canal variant and handles open-end connector updates,
/// solidified-metal drops, and spill sounds on break.
/// </summary>
[BlockRegister]
public partial class BlockMoltenCanal : BlockNetworkNode {
  public override string NetworkType => "molten";

  public override Dictionary<string, string[]> AllowedOrientations { get; } =
    new()
    {
      { "straight", ["ns", "we"] },
      { "bend", ["nw", "se", "en", "ws"] },
      { "tjunction", ["nes", "esw", "swn", "wne"] },
      { "xjunction", ["nswe"] },
    };

  protected override string GetFallbackOrientation(string? type) =>
    type switch {
      "bend" => "nw",
      "tjunction" => "nes",
      "xjunction" => "nswe",
      _ => "ns",
    };

  /// <summary>
  /// Disables wrench rotation (and the hint) while the cell holds liquid metal or has solidified -
  /// drain or chip it clear first.
  /// </summary>
  protected override bool CanWrenchRotate(IWorldAccessor world, BlockPos pos) {
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be
      && (be.HasMoltenMetal || be.Solidified)
    )
      return false;

    return base.CanWrenchRotate(world, pos);
  }

  /// <summary>
  /// Emits incandescent block light scaled to the metal's temperature (via
  /// <see cref="BlockEntityMoltenCanal.GlowLightLevel"/>), like the cowper heat sink.
  /// </summary>
  public override byte[] GetLightHsv(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    ItemStack? stack = null
  ) {
    if (
      pos != null
      && blockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be
    ) {
      byte val = be.GlowLightLevel;
      if (val > 0)
        return [8, 7, val];
    }
    return base.GetLightHsv(blockAccessor, pos, stack);
  }

  public override void OnBlockPlaced(
    IWorldAccessor world,
    BlockPos pos,
    ItemStack? byItemStack
  ) {
    base.OnBlockPlaced(world, pos, byItemStack);
    UpdateEndConnectors(world, pos);
  }

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neibpos
  ) {
    base.OnNeighbourBlockChange(world, pos, neibpos);
    UpdateEndConnectors(world, pos);
  }

  protected void UpdateEndConnectors(IWorldAccessor world, BlockPos pos) {
    if (Orientation == null)
      return;

    var openConnectors = Orientation
      .Where(conn =>
        world.BlockAccessor.GetBlock(
          pos.AddCopy(BlockFacing.FromFirstLetter(conn))
        )
          is not BlockMoltenCanal
      )
      .Select(conn => BlockFacing.FromFirstLetter(conn))
      .ToArray();

    var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityMoltenCanal;
    be?.OpenConnectorFaces = openConnectors;
  }

  public override void OnBlockBroken(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer byPlayer,
    float dropQuantityMultiplier = 1
  ) {
    // Read BE state before base.OnBlockBroken → RemoveNode tears the network down.
    if (
      world.Side == EnumAppSide.Server
      && world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be
      && be.WouldSpillOnRemoval()
    )
      world.PlaySoundAt(ExSounds.Sizzle, pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5);

    base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
  }

  public override ItemStack[] GetDrops(
    IWorldAccessor world,
    BlockPos pos,
    IPlayer? byPlayer,
    float dropQuantityMultiplier = 1f
  ) {
    var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);

    // The network is already torn down here, so read the cached state from the BE (still alive,
    // holding the last broadcast values).
    if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be) {
      var solidifiedDrop = be.GetSolidifiedDrop(world);
      if (solidifiedDrop != null)
        drops = [.. drops, solidifiedDrop];

      // Recover part of the seal's fire clay when a sealed canal is broken.
      if (be.Sealed && world.GetItem(FireClayCode) is { } clay)
        drops =
        [
          .. drops,
          new ItemStack(clay, SmexValues.CanalUnsealClayRefund),
        ];
    }

    return drops;
  }

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos) {
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal {
        Solidified: true
      }
    ) {
      AssetLocation loc = CodeWithVariants(
        ["variant", "state", "orientation"],
        ["pass", "normal", "ns"]
      );
      Block? pickBlock = world.GetBlock(loc);
      return new ItemStack(pickBlock ?? this);
    }

    var drops = GetDrops(world, pos, null);
    return drops.Length > 0 ? drops[0] : new ItemStack(this);
  }

  #region Sealing (separator / valve)
  private static readonly AssetLocation FireClayCode = new("game:clay-fire");

  /// <summary>
  /// Interactions on a molten canal:
  /// <list type="bullet">
  /// <item>Any solidified canal: chisel in hand + hammer in the off-hand chips the
  /// hardened metal out, recovering bits and restoring the run to working order.</item>
  /// <item>Straight canal + <see cref="SmexValues.CanalSealClayCost"/> fire clay:
  /// seals it into a flow-blocking separator.</item>
  /// <item>Sealed straight canal + chisel: breaks the seal and refunds
  /// <see cref="SmexValues.CanalUnsealClayRefund"/> fire clay.</item>
  /// </list>
  /// </summary>
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  ) {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityMoltenCanal be
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    ItemSlot? activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
    ItemStack? held = activeSlot?.Itemstack;

    // A solidified canal is chipped clear with a chisel in hand + hammer in the off-hand (shared
    // chisel-out ritual). A plain click on a still-clogged cell falls through to base.
    if (be.Solidified) {
      return TryChiselClear(world, byPlayer, blockSel, be)
        || base.OnBlockInteractStart(world, byPlayer, blockSel);
    }

    // Sealing / unsealing only applies to straight segments.
    if (Type != "straight")
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (!be.Sealed) {
      if (!IsFireClay(held) || held!.StackSize < SmexValues.CanalSealClayCost)
        return base.OnBlockInteractStart(world, byPlayer, blockSel);

      // Only seal a fully drained section: this cell AND its connector-face neighbours must be
      // empty, so a seal never traps metal against itself.
      if (!CanSeal(world, blockSel.Position, be)) {
        if (world.Side == EnumAppSide.Server)
          (byPlayer as IServerPlayer)?.SendIngameError("smex-canalnotempty");
        return false;
      }

      if (world.Side == EnumAppSide.Server) {
        be.SetSealed(true);
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) {
          activeSlot!.TakeOut(SmexValues.CanalSealClayCost);
          activeSlot.MarkDirty();
        }
        ExSounds.Play(world.Api, blockSel.Position, ExSounds.Build, 0.8f);
      }
      return true;
    }

    // Sealed: unseal with a chisel.
    if (!MoltenChisel.IsTool(held, EnumTool.Chisel))
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (world.Side == EnumAppSide.Server) {
      be.SetSealed(false);
      if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative) {
        Item? clay = world.GetItem(FireClayCode);
        if (clay != null) {
          var refund = new ItemStack(clay, SmexValues.CanalUnsealClayRefund);
          if (!byPlayer.InventoryManager.TryGiveItemstack(refund))
            world.SpawnItemEntity(
              refund,
              blockSel.Position.ToVec3d().Add(0.5, 0.6, 0.5)
            );
        }
        held!.Collectible.DamageItem(world, byPlayer.Entity, activeSlot, 1);
      }
      ExSounds.Play(world.Api, blockSel.Position, ExSounds.StoneCrush, 0.8f);
    }
    return true;
  }

  /// <summary>
  /// Whether this straight canal may be sealed: the cell itself holds no metal and neither does any
  /// of its connector-face canal neighbours. Sealing only severs an already-drained section.
  /// </summary>
  private bool CanSeal(
    IWorldAccessor world,
    BlockPos pos,
    BlockEntityMoltenCanal be
  ) {
    if (!be.IsCellEmpty)
      return false;
    if (Orientation == null)
      return true;

    foreach (char c in Orientation) {
      BlockPos nPos = pos.AddCopy(BlockFacing.FromFirstLetter(c));
      if (
        world.BlockAccessor.GetBlockEntity(nPos) is BlockEntityMoltenCanal nbe
        && !nbe.IsCellEmpty
      )
        return false;
    }
    return true;
  }

  private static bool IsFireClay(ItemStack? stack) =>
    stack?.Collectible?.Code is { } code
    && code.Domain == FireClayCode.Domain
    && code.Path == FireClayCode.Path;

  private static ItemStack[]? _fireClayStacks;

  /// <summary>
  /// Runs the chisel-out on a clogged cell, returning true when the click was a chisel-out (chipped
  /// or refused as too hot). Endpoints call this directly instead of chaining to
  /// <c>base.OnBlockInteractStart</c>: the block and block-entity hierarchies are not parallel - the
  /// pedestal's block derives from the tap's, its block entity from the canal's - so a chained call
  /// lands on a type check it cannot satisfy.
  /// </summary>
  protected static bool TryChiselClear(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel,
    BlockEntityMoltenCanal be
  ) =>
    MoltenChisel.TryChisel(
      world,
      byPlayer,
      blockSel.Position,
      be,
      ExSounds.StoneCrush
    ) != ChiselOutcome.NotChiseling;

  /// <summary>
  /// The "chip out the solidified cell" interaction hint, or <c>null</c> when the cell at
  /// <paramref name="pos"/> can't be chiselled yet (not solidified, or not fully hardened). Exposed so
  /// endpoint subclasses that build their own interaction help (the mold pedestal) can still advertise
  /// clearing a clogged cell - the tap inherits the base help directly.
  /// </summary>
  protected WorldInteraction? ChiselClearInteraction(
    IWorldAccessor world,
    BlockPos pos
  ) =>
    world.BlockAccessor.GetBlockEntity(pos)
      is BlockEntityMoltenCanal { Solidified: true, IsHardened: true }
      ? MoltenChisel.ChiselHelp(world, "smex:blockhelp-canal-clearsolidified")
      : null;

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  ) {
    WorldInteraction[] baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var be =
      world.BlockAccessor.GetBlockEntity(selection.Position)
      as BlockEntityMoltenCanal;

    // The chip-clear hint only shows on a clogged (solidified) cell once it has fully hardened -
    // not on fittings that never latch Solidified, which can't be chiselled.
    if (be is { Solidified: true, IsHardened: true })
      return
      [
        .. baseHelp,
        MoltenChisel.ChiselHelp(world, "smex:blockhelp-canal-clearsolidified"),
      ];

    if (Type != "straight")
      return baseHelp;

    // A sealed canal can always be unsealed (with a chisel).
    if (be is { Sealed: true })
      return
      [
        .. baseHelp,
        MoltenChisel.ChiselHelp(world, "smex:blockhelp-canal-unseal"),
      ];

    // The seal hint only shows when sealing is actually possible: this cell and its neighbours
    // are empty. A cell holding (or sitting next to) metal - liquid or solidified - won't advertise it.
    if (be != null && CanSeal(world, selection.Position, be))
      return
      [
        .. baseHelp,
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-canal-seal",
          MouseButton = EnumMouseButton.Right,
          Itemstacks =
            (_fireClayStacks ??= ResolveFireClayStacks(world)).Length > 0
              ? _fireClayStacks
              : null,
        },
      ];

    return baseHelp;
  }

  private static ItemStack[] ResolveFireClayStacks(IWorldAccessor world) {
    Item? clay = world.GetItem(FireClayCode);
    return clay != null
      ? [new ItemStack(clay, SmexValues.CanalSealClayCost)]
      : [];
  }
  #endregion

  protected static string[] PassOrEndOrientations(string variant) =>
    variant == "pass" ? ["ns", "we"] : ["ns", "we", "ew", "sn"];

  /// <summary>Counts how many horizontal neighbours have a canal connector facing this block.</summary>
  public int CountConnectedNeighborFaces(
    IBlockAccessor blockAccessor,
    BlockPos pos
  ) {
    int count = 0;
    foreach (var face in BlockFacing.HORIZONTALS) {
      BlockPos nPos = pos.AddCopy(face);
      if (
        blockAccessor.GetBlock(nPos) is BlockMoltenCanal nCanal
        && nCanal.HasConnectorAt(face.Opposite)
      )
        count++;
    }
    return count;
  }

  /// <summary>
  /// Chooses the orientation from <paramref name="orientations"/> that best matches
  /// the canal neighbours around <paramref name="pos"/>, or <c>null</c> if none fit.
  /// </summary>
  public static string? PickBestOrientation(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    string[] orientations
  ) {
    var requiredFaces = BlockFacing
      .HORIZONTALS.Where(face => {
        BlockPos nPos = pos.AddCopy(face);
        return blockAccessor.GetBlock(nPos) is BlockMoltenCanal nCanal
          && nCanal.HasConnectorAt(face.Opposite);
      })
      .Select(f => f.Code[0])
      .ToList();

    foreach (string orient in orientations) {
      if (requiredFaces.Count == 1 && orient.StartsWith(requiredFaces[0]))
        return orient;
      else if (
        requiredFaces.Count > 1
        && requiredFaces.TrueForAll(c => orient.Contains(c))
      )
        return orient;
    }

    return null;
  }
}

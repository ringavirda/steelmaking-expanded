using System.Collections.Generic;
using System.Linq;
using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
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
[EntityRegister]
public class BlockMoltenCanal : BlockNetworkNode
{
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
    type switch
    {
      "bend" => "nw",
      "tjunction" => "nes",
      "xjunction" => "nswe",
      _ => "ns",
    };

  /// <summary>
  /// Disables wrench rotation while the cell holds liquid metal or has solidified —
  /// you can't twist a fitting that's full of (or plugged by) metal. Drain or chip
  /// it clear first. Also suppresses the "Rotate" interaction hint in that state.
  /// </summary>
  protected override bool CanWrenchRotate(IWorldAccessor world, BlockPos pos)
  {
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be
      && (be.HasMoltenMetal || be.Solidified)
    )
      return false;

    return base.CanWrenchRotate(world, pos);
  }

  /// <summary>
  /// Emits incandescent block light scaled to the metal's temperature, so a hot
  /// canal lights its surroundings (same scheme as the cowper heat sink). The cell
  /// owns the threshold/scaling via <see cref="BlockEntityMoltenCanal.GlowLightLevel"/>
  /// and re-lights the block when that level shifts.
  /// </summary>
  public override byte[] GetLightHsv(
    IBlockAccessor blockAccessor,
    BlockPos pos,
    ItemStack? stack = null
  )
  {
    if (
      pos != null
      && blockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be
    )
    {
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
  )
  {
    base.OnBlockPlaced(world, pos, byItemStack);
    UpdateEndConnectors(world, pos);
  }

  public override void OnNeighbourBlockChange(
    IWorldAccessor world,
    BlockPos pos,
    BlockPos neibpos
  )
  {
    base.OnNeighbourBlockChange(world, pos, neibpos);
    UpdateEndConnectors(world, pos);
  }

  protected void UpdateEndConnectors(IWorldAccessor world, BlockPos pos)
  {
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
  )
  {
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
  )
  {
    var drops = base.GetDrops(world, pos, byPlayer, dropQuantityMultiplier);

    // Network is already torn down by RemoveNode when GetDrops is called, so read
    // the cached state from the block entity instead — it still holds the last
    // broadcast values and is alive for the duration of this call.
    if (world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal be)
    {
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

  public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
  {
    if (
      world.BlockAccessor.GetBlockEntity(pos) is BlockEntityMoltenCanal
      {
        Solidified: true
      }
    )
    {
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
  )
  {
    if (
      world.BlockAccessor.GetBlockEntity(blockSel.Position)
      is not BlockEntityMoltenCanal be
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    ItemSlot? activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
    ItemStack? held = activeSlot?.Itemstack;

    // A solidified canal (any shape) is chipped clear with a chisel in hand and a
    // hammer in the off-hand — an alternative to breaking and replacing the block.
    if (be.Solidified)
    {
      ItemStack? offhand = byPlayer.Entity?.LeftHandItemSlot?.Itemstack;
      if (!IsTool(held, EnumTool.Chisel) || !IsTool(offhand, EnumTool.Hammer))
        return base.OnBlockInteractStart(world, byPlayer, blockSel);

      // The metal blocks flow as soon as it solidifies, but it can't be chipped
      // out until it has fully hardened (below 0.3 × melting point).
      if (!be.IsHardened)
      {
        if (world.Side == EnumAppSide.Server)
          (byPlayer as IServerPlayer)?.SendIngameError("smex-canaltoohot");
        return true;
      }

      if (world.Side == EnumAppSide.Server)
      {
        ItemStack? recovered = be.ClearSolidified();
        if (
          recovered != null
          && !byPlayer.InventoryManager.TryGiveItemstack(recovered)
        )
          world.SpawnItemEntity(
            recovered,
            blockSel.Position.ToVec3d().Add(0.5, 0.6, 0.5)
          );

        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
          held!.Collectible.DamageItem(world, byPlayer.Entity, activeSlot, 2);

        ExSounds.Play(world.Api, blockSel.Position, ExSounds.StoneCrush, 0.8f);
      }
      return true;
    }

    // Sealing / unsealing only applies to straight segments.
    if (Type != "straight")
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (!be.Sealed)
    {
      if (!IsFireClay(held) || held!.StackSize < SmexValues.CanalSealClayCost)
        return base.OnBlockInteractStart(world, byPlayer, blockSel);

      // Don't let a player cut a line that still has liquid metal in it, or one
      // that has solidified (chip it clear before sealing).
      if (be.HasMoltenMetal || be.Solidified)
      {
        if (world.Side == EnumAppSide.Server)
          (byPlayer as IServerPlayer)?.SendIngameError("smex-canalnotempty");
        return false;
      }

      if (world.Side == EnumAppSide.Server)
      {
        be.SetSealed(true);
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
          activeSlot!.TakeOut(SmexValues.CanalSealClayCost);
          activeSlot.MarkDirty();
        }
        ExSounds.Play(world.Api, blockSel.Position, ExSounds.Build, 0.8f);
      }
      return true;
    }

    // Sealed: unseal with a chisel.
    if (!IsTool(held, EnumTool.Chisel))
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    if (world.Side == EnumAppSide.Server)
    {
      be.SetSealed(false);
      if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
      {
        Item? clay = world.GetItem(FireClayCode);
        if (clay != null)
        {
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

  private static bool IsFireClay(ItemStack? stack) =>
    stack?.Collectible?.Code is { } code
    && code.Domain == FireClayCode.Domain
    && code.Path == FireClayCode.Path;

  private static bool IsTool(ItemStack? stack, EnumTool tool) =>
    stack?.Collectible?.Tool == tool;

  private static ItemStack[]? _fireClayStacks;
  private static ItemStack[]? _chiselStacks;

  public override WorldInteraction[] GetPlacedBlockInteractionHelp(
    IWorldAccessor world,
    BlockSelection selection,
    IPlayer forPlayer
  )
  {
    WorldInteraction[] baseHelp =
      base.GetPlacedBlockInteractionHelp(world, selection, forPlayer) ?? [];

    var be =
      world.BlockAccessor.GetBlockEntity(selection.Position)
      as BlockEntityMoltenCanal;

    // A solidified canal (any shape) is chipped clear with a chisel + hammer, but
    // only once its metal has fully hardened (below 0.3 × melting point).
    if (be?.IsHardened == true)
      return
      [
        .. baseHelp,
        new WorldInteraction
        {
          ActionLangCode = "smex:blockhelp-canal-clearsolidified",
          MouseButton = EnumMouseButton.Right,
          Itemstacks = _chiselStacks ??=
            [
              .. world
                .SearchItems(new AssetLocation("chisel-*"))
                .Select(i => new ItemStack(i)),
            ],
        },
      ];

    if (Type != "straight")
      return baseHelp;

    bool isSealed = be is { Sealed: true };

    WorldInteraction extra = isSealed
      ? new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-canal-unseal",
        MouseButton = EnumMouseButton.Right,
        Itemstacks = _chiselStacks ??=
          [
            .. world
              .SearchItems(new AssetLocation("chisel-*"))
              .Select(i => new ItemStack(i)),
          ],
      }
      : new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-canal-seal",
        MouseButton = EnumMouseButton.Right,
        Itemstacks =
          (_fireClayStacks ??= ResolveFireClayStacks(world)).Length > 0
            ? _fireClayStacks
            : null,
      };

    return [.. baseHelp, extra];
  }

  private static ItemStack[] ResolveFireClayStacks(IWorldAccessor world)
  {
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
  )
  {
    int count = 0;
    foreach (var face in BlockFacing.HORIZONTALS)
    {
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
  )
  {
    var requiredFaces = BlockFacing
      .HORIZONTALS.Where(face =>
      {
        BlockPos nPos = pos.AddCopy(face);
        return blockAccessor.GetBlock(nPos) is BlockMoltenCanal nCanal
          && nCanal.HasConnectorAt(face.Opposite);
      })
      .Select(f => f.Code[0])
      .ToList();

    foreach (string orient in orientations)
    {
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

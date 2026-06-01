using System.Collections.Generic;
using System.Linq;
using BlockNetworkLib;
using SteelmakingExpanded.Networks.Molten.BlockEntities;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.Networks.Molten.Blocks;

/// <summary>
/// The base molten-canal block: a self-orienting node of the "molten" network that
/// carries liquid metal. Provides the orientation tables shared by every
/// straight/bend/junction canal variant and handles open-end connector updates,
/// solidified-metal drops, and spill sounds on break.
/// </summary>
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
      world.PlaySoundAt(
        SmexSounds.Sizzle,
        pos.X + 0.5,
        pos.Y + 0.5,
        pos.Z + 0.5
      );

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
        drops = [.. drops, new ItemStack(clay, UnsealClayRefund)];
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
  /// <summary>Fire clay consumed to seal a straight canal into a separator.</summary>
  private const int SealClayCost = 4;

  /// <summary>Fire clay returned when a seal is chiselled back out.</summary>
  private const int UnsealClayRefund = 2;

  private static readonly AssetLocation FireClayCode = new("game:clay-fire");

  /// <summary>
  /// Right-click a straight canal with <see cref="SealClayCost"/> fire clay to seal it
  /// into a flow-blocking separator; right-click a sealed canal with a chisel in hand
  /// and a hammer in the off-hand to break the seal and recover <see cref="UnsealClayRefund"/>
  /// fire clay. Only straight segments are sealable.
  /// </summary>
  public override bool OnBlockInteractStart(
    IWorldAccessor world,
    IPlayer byPlayer,
    BlockSelection blockSel
  )
  {
    if (
      Type != "straight"
      || world.BlockAccessor.GetBlockEntity(blockSel.Position)
        is not BlockEntityMoltenCanal be
    )
      return base.OnBlockInteractStart(world, byPlayer, blockSel);

    ItemSlot? activeSlot = byPlayer.InventoryManager.ActiveHotbarSlot;
    ItemStack? held = activeSlot?.Itemstack;

    if (!be.Sealed)
    {
      if (!IsFireClay(held) || held!.StackSize < SealClayCost)
        return base.OnBlockInteractStart(world, byPlayer, blockSel);

      // Don't let a player cut a line that still has liquid metal in it.
      if (be.HasMoltenMetal)
      {
        if (world.Side == EnumAppSide.Server)
          (byPlayer as IServerPlayer)?.SendIngameError(
            "canalnotempty",
            "smex:canal-err-sealnotempty"
          );
        return false;
      }

      if (world.Side == EnumAppSide.Server)
      {
        be.SetSealed(true);
        if (byPlayer.WorldData.CurrentGameMode != EnumGameMode.Creative)
        {
          activeSlot!.TakeOut(SealClayCost);
          activeSlot.MarkDirty();
        }
        SmexSounds.Play(world.Api, blockSel.Position, SmexSounds.Build, 0.8f);
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
          var refund = new ItemStack(clay, UnsealClayRefund);
          if (!byPlayer.InventoryManager.TryGiveItemstack(refund))
            world.SpawnItemEntity(
              refund,
              blockSel.Position.ToVec3d().Add(0.5, 0.6, 0.5)
            );
        }
        held!.Collectible.DamageItem(world, byPlayer.Entity, activeSlot, 1);
      }
      SmexSounds.Play(
        world.Api,
        blockSel.Position,
        SmexSounds.StoneCrush,
        0.8f
      );
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

    if (Type != "straight")
      return baseHelp;

    bool isSealed =
      world.BlockAccessor.GetBlockEntity(selection.Position)
      is BlockEntityMoltenCanal { Sealed: true };

    WorldInteraction extra = isSealed
      ? new WorldInteraction
      {
        ActionLangCode = "smex:blockhelp-canal-unseal",
        MouseButton = EnumMouseButton.Right,
        Itemstacks = _chiselStacks ??=
        [
          .. world.SearchItems(new AssetLocation("chisel-*")).Select(i =>
            new ItemStack(i)
          ),
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
    return clay != null ? [new ItemStack(clay, SealClayCost)] : [];
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

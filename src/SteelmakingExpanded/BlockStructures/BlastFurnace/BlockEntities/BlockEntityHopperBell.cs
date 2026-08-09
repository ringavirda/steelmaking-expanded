using System;
using System.Text;
using ExpandedLib.Helpers;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.Compat;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>
/// Block entity for the bell hopper beneath the reinforced hopper. Crafts burden
/// from the iron/coke/flux in the hopper above into an internal magazine, then drops
/// it into the furnace shaft below while dropping is enabled.
/// </summary>
[BlockEntityRegister]
public class BlockEntityHopperBell : BlockEntity {
  private long _tickId;
  private Item? _burdenItem;
  private int _burdenMagazine = 0;

  // Feed over-spent on the last batch because the last piece could not be split, banked against the
  // next one. Always under one piece of the bulkier feed, so a run of mixed batches costs exactly
  // the advertised rate per item instead of losing a fraction to rounding every time.
  private int _oreCredit = 0;
  private int _carbonCredit = 0;

  // Dropping is on by default so a freshly built furnace feeds itself without the
  // player having to discover the Ctrl + right-click toggle first.
  private bool _isDropping = true;

  /// <summary>Burden currently buffered in the hopper's internal magazine.</summary>
  public int BurdenMagazine => _burdenMagazine;

  /// <summary>Maximum burden the magazine can hold.</summary>
  public int MaxMagazineCapacity => SmexValues.HopperMaxMagazineCapacity;

  /// <summary>Whether the hopper is dropping burden into the furnace.</summary>
  public bool IsDropping {
    get => _isDropping;
    set {
      if (_isDropping == value)
        return;
      _isDropping = value;
      if (Api?.Side == EnumAppSide.Server) {
        if (_isDropping)
          StartTicking();
        else
          StopTicking();
      }
    }
  }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);

    if (api.Side == EnumAppSide.Server) {
      _burdenItem = api.World.GetItem(new AssetLocation("smex", "burden"));
      if (_isDropping)
        StartTicking();
    }
  }

  private void StartTicking() {
    if (_tickId == 0 && Api != null)
      _tickId = RegisterGameTickListener(OnServerTick, 1000);
  }

  private void StopTicking() {
    if (_tickId != 0 && Api != null) {
      UnregisterGameTickListener(_tickId);
      _tickId = 0;
    }
  }

  public override void FromTreeAttributes(
    ITreeAttribute tree,
    IWorldAccessor worldForResolving
  ) {
    base.FromTreeAttributes(tree, worldForResolving);
    int oldMagazine = _burdenMagazine;
    // Falls back to the pre-rename key so a stocked magazine survives the upgrade.
    _burdenMagazine = tree.GetInt(
      "burdenMagazine",
      tree.GetInt("blastMixMagazine")
    );
    IsDropping = tree.GetBool("isDropping", true);
    _oreCredit = tree.GetInt("oreCredit");
    _carbonCredit = tree.GetInt("carbonCredit");

    // The reinforced hopper above renders its contents pile from our magazine level,
    // so nudge it to re-tessellate whenever that level changes on the client.
    if (oldMagazine != _burdenMagazine && Api?.Side == EnumAppSide.Client) {
      Api.World.BlockAccessor.GetBlockEntity(Pos.UpCopy())?.MarkDirty(true);
    }
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetInt("burdenMagazine", _burdenMagazine);
    tree.SetBool("isDropping", IsDropping);
    tree.SetInt("oreCredit", _oreCredit);
    tree.SetInt("carbonCredit", _carbonCredit);
  }

  private void OnServerTick(float dt) {
    if (
      Api.World.BlockAccessor.GetBlockEntity(Pos.UpCopy())
      is not BlockEntityHopperReinforced topHopper
    )
      return;

    var inv = topHopper.Inventory;
    if (inv == null)
      return;

    // Set when this tick took anything out of the hopper above, so the sync below costs one packet
    // per tick rather than one per craft cycle.
    bool feedChanged = false;

    int limeReq = SmexValues.HopperLimeRequired;
    int burdenProd = SmexValues.HopperBurdenProduced;
    int dropAmount = SmexValues.HopperDropAmount;

    // Reclaimed burden sitting in the hopper feeds straight into the magazine
    // (1:1), taking priority over crafting fresh burden from ore.
    int magazineSpace = MaxMagazineCapacity - _burdenMagazine;
    if (magazineSpace > 0) {
      int reclaim = System.Math.Min(magazineSpace, CountItems(inv, IsBurden));
      if (reclaim > 0) {
        ConsumeItems(inv, IsBurden, reclaim);
        _burdenMagazine += reclaim;
        feedChanged = true;
        MarkDirty(true);
      }
    }

    while (_burdenMagazine <= MaxMagazineCapacity - burdenProd) {
      if (
        CountOre(inv) + _oreCredit >= BurdenValue.OrePerBatch
        && CountCarbon(inv) + _carbonCredit >= BurdenValue.CarbonPerBatch
        && CountItems(inv, IsLime) >= limeReq
      ) {
        ConsumeOre(inv);
        ConsumeCarbon(inv);
        ConsumeItems(inv, IsLime, limeReq);

        _burdenMagazine += burdenProd;
        feedChanged = true;
        MarkDirty(true);
      } else {
        break;
      }
    }

    if (_burdenMagazine >= dropAmount && !IsFurnaceFull()) {
      BlockPos? targetPos = FindBestPileLocation(dropAmount);
      if (targetPos != null) {
        DropBurden(targetPos, dropAmount);
        _burdenMagazine -= dropAmount;
        MarkDirty(true);
      }
    }

    // The feed inventory belongs to the hopper ABOVE, and the client reads it from THAT block
    // entity's tree - MarkDirty is per position, so marking ourselves only ever shipped the
    // magazine. slot.MarkDirty() alone reaches a client only while that client has the dialog
    // open, which is why the contents looked right until you closed and reopened it, and why
    // relogging cleared it. Not MarkDirty(true): the hopper's pile mesh is driven by the bell's
    // magazine, so a retessellation here would be wasted every second the furnace is fed.
    if (feedChanged)
      topHopper.MarkDirty();
  }

  /// <summary>Returns <c>true</c> when the furnace shaft below has no room for more burden.</summary>
  public bool IsFurnaceFull() {
    if (Api == null)
      return false;

    Block b = Api.World.BlockAccessor.GetBlock(Pos.DownCopy(2));
    if (b.Code?.Path.StartsWith("coalpile") != true)
      return false;

    BlockPos planeCenter = Pos.DownCopy(3);
    for (int dx = -1; dx <= 1; dx++) {
      for (int dz = -1; dz <= 1; dz++) {
        Block planeBlock = Api.World.BlockAccessor.GetBlock(
          planeCenter.AddCopy(dx, 0, dz)
        );
        if (planeBlock.Code?.Path.StartsWith("coalpile") != true)
          return false;
      }
    }

    return true;
  }

  private BlockPos? FindBestPileLocation(int dropAmount) {
    int maxDepth = 15;
    int floorY = Pos.Y;

    for (int d = 2; d <= maxDepth; d++) {
      BlockPos checkPos = Pos.DownCopy(d);
      Block b = Api.World.BlockAccessor.GetBlock(checkPos);

      if (b.Replaceable < 6000 && b.Code?.Path.StartsWith("coalpile") != true) {
        floorY = checkPos.Y + 1;
        break;
      }
    }

    for (int y = floorY; y < Pos.Y; y++) {
      BlockPos centerPos = new BlockPos(Pos.X, y, Pos.Z);

      if (IsValidPileTarget(centerPos, dropAmount))
        return centerPos;

      BlockPos[] neighbors =
      [
        centerPos.AddCopy(1, 0, 0),
        centerPos.AddCopy(-1, 0, 0),
        centerPos.AddCopy(0, 0, 1),
        centerPos.AddCopy(0, 0, -1),
        centerPos.AddCopy(1, 0, 1),
        centerPos.AddCopy(-1, 0, -1),
        centerPos.AddCopy(1, 0, -1),
        centerPos.AddCopy(-1, 0, 1),
      ];

      foreach (var n in neighbors) {
        if (IsValidPileTarget(n, dropAmount))
          return n;
      }
    }

    return null;
  }

  private bool IsValidPileTarget(BlockPos pos, int dropAmount) {
    Block b = Api.World.BlockAccessor.GetBlock(pos);

    if (b.Replaceable >= 6000)
      return true;

    if (b.Code?.Path.StartsWith("coalpile") == true) {
      if (
        Api.World.BlockAccessor.GetBlockEntity(pos)
        is BlockEntityItemPile pileBe
      ) {
        var slot = pileBe.inventory[0];
        if (slot.Empty)
          return true;

        if (slot.Itemstack.Collectible.Code.Path.Equals("burden")) {
          if (slot.StackSize + dropAmount <= pileBe.MaxStackSize)
            return true;
        }
      }
    }

    return false;
  }

  private void DropBurden(BlockPos targetPos, int amount) {
    if (_burdenItem == null)
      return;

    Block blockAtTarget = Api.World.BlockAccessor.GetBlock(targetPos);

    if (blockAtTarget.Replaceable >= 6000) {
      Block? coalPileBlock = Api.World.GetBlock(
        new AssetLocation("game", "coalpile")
      );
      if (coalPileBlock != null) {
        Api.World.BlockAccessor.SetBlock(coalPileBlock.BlockId, targetPos);
        blockAtTarget = coalPileBlock;
      }
    }

    if (blockAtTarget.Code?.Path.StartsWith("coalpile") == true) {
      if (
        Api.World.BlockAccessor.GetBlockEntity(targetPos)
        is BlockEntityItemPile pileBe
      ) {
        var slot = pileBe.inventory[0];

        if (slot.Empty) {
          slot.Itemstack = new ItemStack(_burdenItem, amount);
        } else {
          slot.Itemstack.StackSize += amount;
        }

        slot.MarkDirty();
        pileBe.MarkDirty(true);

        Api.World.BlockAccessor.MarkBlockDirty(targetPos);
      }
    }

    SpawnFallingParticles();
    Api.World.PlaySoundAt(ExSounds.StoneCrush, Pos.X, Pos.Y, Pos.Z);
  }

  private void SpawnFallingParticles() =>
    ExParticles.FallingDust(Api.World, Pos);

  private bool IsBurden(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("burden");

  private bool IsCrushedIronOre(ItemStack stack) =>
    IronOreCompat.IsCrushedIronOre(stack.Collectible.Code.Path);

  private bool IsIronNugget(ItemStack stack) =>
    IronOreCompat.IsIronNugget(stack.Collectible.Code.Path);

  // Coke goes into the burden whole. The crushed intermediate is retired: it existed only to be
  // fed here, and its pulverizer route collided with other mods' crushing economies.
  private bool IsCoke(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("coke");

  private bool IsCharcoal(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("charcoal");

  #region Burden carbon

  /// <summary>Total carbon units the hopper's fuel slots currently hold.</summary>
  private int CountCarbon(InventoryBase inv) =>
    CountItems(inv, IsCoke) * BurdenValue.CarbonPerCoke
    + CountItems(inv, IsCharcoal) * BurdenValue.CarbonPerCharcoal;

  /// <summary>
  /// Takes one batch worth of carbon, spending coke first so the denser fuel is used up before
  /// the bulkier one.
  /// </summary>
  private void ConsumeCarbon(InventoryBase inv) =>
    _carbonCredit = TakeUnits(
      inv,
      BurdenValue.CarbonPerBatch - _carbonCredit,
      IsCoke,
      BurdenValue.CarbonPerCoke,
      IsCharcoal,
      BurdenValue.CarbonPerCharcoal
    );

  #endregion

  #region Burden ore

  /// <summary>Total ore units the hopper's iron slots currently hold.</summary>
  private int CountOre(InventoryBase inv) =>
    CountItems(inv, IsCrushedIronOre) * BurdenValue.OrePerCrushed
    + CountItems(inv, IsIronNugget) * BurdenValue.OrePerNugget;

  /// <summary>
  /// Takes one batch worth of ore, spending crushed ore before raw nuggets so the prepared feed
  /// is used up first.
  /// </summary>
  private void ConsumeOre(InventoryBase inv) =>
    _oreCredit = TakeUnits(
      inv,
      BurdenValue.OrePerBatch - _oreCredit,
      IsCrushedIronOre,
      BurdenValue.OrePerCrushed,
      IsIronNugget,
      BurdenValue.OrePerNugget
    );

  #endregion

  /// <summary>
  /// Takes <paramref name="needed"/> units from two interchangeable feeds, spending the denser one
  /// first, and returns whatever it had to over-spend to get there.
  /// <para>
  /// The last piece of the bulkier feed cannot be split, so a remainder that is not a whole number
  /// of them is rounded up - at the shipped rates a mixed batch of crushed ore and nuggets can cost
  /// up to 8 ore units more than the batch is worth. The surplus is returned rather than discarded
  /// so the caller can bank it against the next batch, which is what keeps a long run of mixed
  /// feeds paying exactly the advertised rate per item.
  /// </para>
  /// </summary>
  private static int TakeUnits(
    InventoryBase inv,
    int needed,
    System.Func<ItemStack, bool> dense,
    int densePerItem,
    System.Func<ItemStack, bool> bulky,
    int bulkyPerItem
  ) {
    int denseTaken = Math.Min(CountItems(inv, dense), needed / densePerItem);
    if (denseTaken > 0) {
      ConsumeItems(inv, dense, denseTaken);
      needed -= denseTaken * densePerItem;
    }

    if (needed <= 0)
      return -needed;

    int bulkyTaken = (needed + bulkyPerItem - 1) / bulkyPerItem;
    ConsumeItems(inv, bulky, bulkyTaken);
    return bulkyTaken * bulkyPerItem - needed;
  }

  private bool IsLime(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("lime");

  private static int CountItems(
    InventoryBase inv,
    System.Func<ItemStack, bool> matcher
  ) {
    int count = 0;
    foreach (var slot in inv) {
      if (!slot.Empty && matcher(slot.Itemstack))
        count += slot.StackSize;
    }
    return count;
  }

  private static void ConsumeItems(
    InventoryBase inv,
    System.Func<ItemStack, bool> matcher,
    int amountToTake
  ) {
    int remaining = amountToTake;
    foreach (var slot in inv) {
      if (slot.Empty || !matcher(slot.Itemstack))
        continue;

      int taken = Math.Min(remaining, slot.StackSize);
      slot.TakeOut(taken);
      slot.MarkDirty();

      remaining -= taken;
      if (remaining <= 0)
        break;
    }
  }

  public override void OnBlockRemoved() {
    base.OnBlockRemoved();
    StopTicking();
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);
    dsc.AppendLine(
      Lang.Get(
        "smex:hopper-info-bell",
        IsDropping
          ? Lang.Get("smex:hopper-state-dropping")
          : Lang.Get("smex:hopper-state-stopped")
      )
    );
    dsc.AppendLine(
      Lang.Get("smex:hopper-info-magazine", BurdenMagazine, MaxMagazineCapacity)
    );
  }
}

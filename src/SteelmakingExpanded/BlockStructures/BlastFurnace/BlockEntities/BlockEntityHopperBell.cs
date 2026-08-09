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
/// Block entity for the bell hopper beneath the reinforced hopper. Crafts blast mix
/// from the iron/coke/flux in the hopper above into an internal magazine, then drops
/// it into the furnace shaft below while dropping is enabled.
/// </summary>
[BlockEntityRegister]
public class BlockEntityHopperBell : BlockEntity {
  private long _tickId;
  private Item? _blastMixItem;
  private int _blastMixMagazine = 0;

  // Dropping is on by default so a freshly built furnace feeds itself without the
  // player having to discover the Ctrl + right-click toggle first.
  private bool _isDropping = true;

  /// <summary>Blast mix currently buffered in the hopper's internal magazine.</summary>
  public int BlastMixMagazine => _blastMixMagazine;

  /// <summary>Maximum blast mix the magazine can hold.</summary>
  public int MaxMagazineCapacity => SmexValues.HopperMaxMagazineCapacity;

  /// <summary>Whether the hopper is dropping blast mix into the furnace.</summary>
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
      _blastMixItem = api.World.GetItem(new AssetLocation("smex", "blastmix"));
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
    int oldMagazine = _blastMixMagazine;
    _blastMixMagazine = tree.GetInt("blastMixMagazine");
    IsDropping = tree.GetBool("isDropping", true);

    // The reinforced hopper above renders its contents pile from our magazine level,
    // so nudge it to re-tessellate whenever that level changes on the client.
    if (oldMagazine != _blastMixMagazine && Api?.Side == EnumAppSide.Client) {
      Api.World.BlockAccessor.GetBlockEntity(Pos.UpCopy())?.MarkDirty(true);
    }
  }

  public override void ToTreeAttributes(ITreeAttribute tree) {
    base.ToTreeAttributes(tree);
    tree.SetInt("blastMixMagazine", _blastMixMagazine);
    tree.SetBool("isDropping", IsDropping);
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

    int ironOreReq = SmexValues.HopperIronOreRequired;
    int limeReq = SmexValues.HopperLimeRequired;
    int blastmixProd = SmexValues.HopperBlastmixProduced;
    int dropAmount = SmexValues.HopperDropAmount;

    // Reclaimed blastmix sitting in the hopper feeds straight into the magazine
    // (1:1), taking priority over crafting fresh blastmix from ore.
    int magazineSpace = MaxMagazineCapacity - _blastMixMagazine;
    if (magazineSpace > 0) {
      int reclaim = System.Math.Min(magazineSpace, CountItems(inv, IsBlastmix));
      if (reclaim > 0) {
        ConsumeItems(inv, IsBlastmix, reclaim);
        _blastMixMagazine += reclaim;
        feedChanged = true;
        MarkDirty(true);
      }
    }

    while (_blastMixMagazine <= MaxMagazineCapacity - blastmixProd) {
      if (
        CountItems(inv, IsIronOre) >= ironOreReq
        && CountCarbon(inv) >= CarbonPerBatch
        && CountItems(inv, IsLime) >= limeReq
      ) {
        ConsumeItems(inv, IsIronOre, ironOreReq);
        ConsumeCarbon(inv);
        ConsumeItems(inv, IsLime, limeReq);

        _blastMixMagazine += blastmixProd;
        feedChanged = true;
        MarkDirty(true);
      } else {
        break;
      }
    }

    if (_blastMixMagazine >= dropAmount && !IsFurnaceFull()) {
      BlockPos? targetPos = FindBestPileLocation(dropAmount);
      if (targetPos != null) {
        DropBlastMix(targetPos, dropAmount);
        _blastMixMagazine -= dropAmount;
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

  /// <summary>Returns <c>true</c> when the furnace shaft below has no room for more blast mix.</summary>
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

        if (slot.Itemstack.Collectible.Code.Path.Equals("blastmix")) {
          if (slot.StackSize + dropAmount <= 16)
            return true;
        }
      }
    }

    return false;
  }

  private void DropBlastMix(BlockPos targetPos, int amount) {
    if (_blastMixItem == null)
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
          slot.Itemstack = new ItemStack(_blastMixItem, amount);
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

  private bool IsBlastmix(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("blastmix");

  private bool IsIronOre(ItemStack stack) =>
    IronOreCompat.IsCrushedIronOre(stack.Collectible.Code.Path);

  // Coke goes into the burden whole. The crushed intermediate is retired: it existed only to be
  // fed here, and its pulverizer route collided with other mods' crushing economies.
  private bool IsCoke(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("coke");

  private bool IsCharcoal(ItemStack stack) =>
    stack.Collectible.Code.Path.Equals("charcoal");

  #region Burden carbon

  // The two fuels are counted in a shared unit rather than as separate quotas, so a batch can be
  // fed coke, charcoal or any mix of the two at one exchange rate. Scaling by BOTH configured
  // amounts keeps the arithmetic exact in integers whatever the two are set to: at the defaults
  // (2 coke or 4 charcoal) a batch costs 8 units, coke is worth 4 and charcoal 2.

  /// <summary>Carbon units one piece of coke contributes.</summary>
  private static int CarbonPerCoke => SmexValues.HopperCharcoalRequired;

  /// <summary>Carbon units one piece of charcoal contributes.</summary>
  private static int CarbonPerCharcoal => SmexValues.HopperCokeRequired;

  /// <summary>Carbon units one blast-mix batch costs.</summary>
  private static int CarbonPerBatch =>
    SmexValues.HopperCokeRequired * SmexValues.HopperCharcoalRequired;

  /// <summary>Total carbon units the hopper's fuel slots currently hold.</summary>
  private int CountCarbon(InventoryBase inv) =>
    CountItems(inv, IsCoke) * CarbonPerCoke
    + CountItems(inv, IsCharcoal) * CarbonPerCharcoal;

  /// <summary>
  /// Takes one batch worth of carbon, spending coke first so the denser fuel is used up before
  /// the bulkier one. The charcoal remainder is rounded up to a whole piece, which can only
  /// over-spend when the two configured amounts do not divide evenly.
  /// </summary>
  private void ConsumeCarbon(InventoryBase inv) {
    int needed = CarbonPerBatch;

    int cokeAvailable = CountItems(inv, IsCoke);
    int cokeTaken = Math.Min(cokeAvailable, needed / CarbonPerCoke);
    if (cokeTaken > 0) {
      ConsumeItems(inv, IsCoke, cokeTaken);
      needed -= cokeTaken * CarbonPerCoke;
    }

    if (needed <= 0)
      return;

    int charcoalTaken = (needed + CarbonPerCharcoal - 1) / CarbonPerCharcoal;
    ConsumeItems(inv, IsCharcoal, charcoalTaken);
  }

  #endregion

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
      Lang.Get(
        "smex:hopper-info-magazine",
        BlastMixMagazine,
        MaxMagazineCapacity
      )
    );
  }
}

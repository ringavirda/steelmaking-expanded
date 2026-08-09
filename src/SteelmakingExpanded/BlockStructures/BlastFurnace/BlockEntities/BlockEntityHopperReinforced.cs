using System.Text;
using ExpandedLib.Registries.Entities;
using SteelmakingExpanded.Compat;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockStructures.BlastFurnace.BlockEntities;

/// <summary>
/// Block entity for the reinforced hopper: an 8-slot container holding the iron ore,
/// coke and flux that the bell hopper below consumes to craft blast mix. Right-click
/// opens its dialog; Ctrl + right-click toggles the bell hopper's dropping.
/// </summary>
[BlockEntityRegister]
public class BlockEntityHopperReinforced : BlockEntityContainer {
  private InventoryGeneric _inventory;

  // The open inventory dialog (client-side only; null when closed).
  private GuiDialogHopper? _invDialog;

  // Block-entity packet ids for the open/close handshake, matching the vanilla
  // openable-container protocol (see OnReceivedClientPacket).
  private const int PacketIdOpen = 1000;
  private const int PacketIdClose = 1001;

  // Cached, untranslated mesh of the blast-mix contents pile (built lazily client-side).
  private MeshData? _contentsBaseMesh;

  // The contents pile is drawn between these heights (in 1/16 block units) inside the
  // hopper, scaling with how full the bell hopper's magazine is below.
  private const float ContentsMinY = 9f;
  private const float ContentsMaxY = 14f;

  public override InventoryBase Inventory => _inventory;
  public override string InventoryClassName => "hopperreinforced";

  public BlockEntityHopperReinforced() {
    _inventory = new InventoryBlastFurnace(8, "hopperreinforced-0", null, null);
  }

  public override void Initialize(ICoreAPI api) {
    base.Initialize(api);
    _inventory.LateInitialize(
      InventoryClassName + "-" + Pos.X + "/" + Pos.Y + "/" + Pos.Z,
      api
    );

    // Machines mutate this inventory directly - the bell hopper below, and any vanilla chute
    // pushing in through the Container behavior. Both mark only the SLOT, which the engine
    // delivers solely to players who have the dialog open at that moment; a client with it closed
    // reads these contents from this block entity's tree and would keep a stale copy until relog.
    // No unsubscribe needed: the inventory does not outlive the block entity. No feedback loop
    // either - loading the tree assigns slots directly and never raises this event.
    if (api.Side == EnumAppSide.Server)
      _inventory.SlotModified += OnServerSlotModified;
  }

  private void OnServerSlotModified(int slotId) => MarkDirty();

  /// <summary>Opens the hopper inventory, or (with Ctrl held) toggles dropping on the bell hopper below.</summary>
  public void OnInteract(IPlayer byPlayer) {
    if (byPlayer.Entity.Controls.CtrlKey) {
      if (
        Api.Side == EnumAppSide.Server
        && Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy())
          is BlockEntityHopperBell bell
      ) {
        bell.IsDropping = !bell.IsDropping;
        bell.MarkDirty(true);
      }
      return;
    }

    // The dialog lives entirely on the client. The server opens/closes the inventory
    // and applies slot moves through the open/close/slot packets handled in
    // OnReceivedClientPacket - without that handshake the server never registers the
    // clicks, so the client and server inventories silently diverge.
    if (Api.Side == EnumAppSide.Client)
      ToggleDialog((ICoreClientAPI)Api, byPlayer);
  }

  private void ToggleDialog(ICoreClientAPI capi, IPlayer byPlayer) {
    if (_invDialog != null) {
      _invDialog.TryClose();
      return;
    }

    _invDialog = new GuiDialogHopper(
      Lang.Get("smex:hopper-dialog-title"),
      Inventory,
      Pos,
      capi
    );
    _invDialog.OnClosed += () => {
      _invDialog = null;
      capi.Network.SendBlockEntityPacket(Pos, PacketIdClose);
    };
    _invDialog.TryOpen();

    capi.Network.SendPacketClient(Inventory.Open(byPlayer));
    capi.Network.SendBlockEntityPacket(Pos, PacketIdOpen);
  }

  /// <summary>
  /// Server-side handling of the inventory dialog packets. The base
  /// <see cref="BlockEntityContainer"/> does not route these, so slot moves from the
  /// dialog grid would otherwise be dropped, leaving the server inventory out of sync
  /// with what the player sees. Mirrors the vanilla openable-container protocol.
  /// </summary>
  public override void OnReceivedClientPacket(
    IPlayer player,
    int packetid,
    byte[] data
  ) {
    if (packetid == PacketIdClose) {
      player.InventoryManager?.CloseInventory(Inventory);
      return;
    }

    if (!Api.World.Claims.TryAccess(player, Pos, EnumBlockAccessFlags.Use)) {
      Api.World.Logger.Audit(
        "Player {0} sent a hopper inventory packet to {1} without claim access. Rejected.",
        player.PlayerName,
        Pos
      );
      return;
    }

    if (packetid < 1000) {
      Inventory.InvNetworkUtil.HandleClientPacket(player, packetid, data);
      return;
    }

    if (packetid == PacketIdOpen) {
      player.InventoryManager?.OpenInventory(Inventory);
      return;
    }

    base.OnReceivedClientPacket(player, packetid, data);
  }

  public override void GetBlockInfo(IPlayer forPlayer, StringBuilder dsc) {
    base.GetBlockInfo(forPlayer, dsc);

    if (
      Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy())
      is BlockEntityHopperBell bell
    ) {
      dsc.AppendLine(
        Lang.Get(
          "smex:hopper-info-bell",
          bell.IsDropping
            ? Lang.Get("smex:hopper-state-dropping")
            : Lang.Get("smex:hopper-state-stopped")
        )
      );
      dsc.AppendLine(
        Lang.Get(
          "smex:hopper-info-magazine",
          bell.BlastMixMagazine,
          bell.MaxMagazineCapacity
        )
      );

      if (bell.IsFurnaceFull()) {
        dsc.AppendLine(Lang.Get("smex:hopper-info-furnacefull"));
      }
    } else {
      dsc.AppendLine(Lang.Get("smex:hopper-info-nobell"));
    }
  }

  /// <summary>
  /// Draws the blast-mix contents pile on top of the normal hopper mesh, raised
  /// between <see cref="ContentsMinY"/> and <see cref="ContentsMaxY"/> in proportion
  /// to how full the bell hopper's magazine is below. The bell re-triggers this
  /// tessellation whenever its magazine changes.
  /// </summary>
  public override bool OnTesselation(
    ITerrainMeshPool mesher,
    ITesselatorAPI tesselator
  ) {
    if (
      Api.World.BlockAccessor.GetBlockEntity(Pos.DownCopy())
        is BlockEntityHopperBell bell
      && bell.BlastMixMagazine > 0
    ) {
      _contentsBaseMesh ??= BuildContentsMesh(tesselator);
      if (_contentsBaseMesh != null) {
        float fill = GameMath.Clamp(
          (float)bell.BlastMixMagazine / bell.MaxMagazineCapacity,
          0f,
          1f
        );
        float yOffset =
          (ContentsMinY + fill * (ContentsMaxY - ContentsMinY)) / 16f;

        MeshData mesh = _contentsBaseMesh.Clone();
        mesh.Translate(0f, yOffset, 0f);
        mesher.AddMeshData(mesh);
      }
    }

    // Keep the default hopper block mesh as well.
    return base.OnTesselation(mesher, tesselator);
  }

  private MeshData? BuildContentsMesh(ITesselatorAPI tesselator) {
    Shape? shape = Api
      .Assets.TryGet(
        new AssetLocation("smex:shapes/blastfurnace/contents-blastmix.json")
      )
      ?.ToObject<Shape>();
    if (shape == null)
      return null;

    tesselator.TesselateShape(Block, shape, out MeshData mesh);
    return mesh;
  }
}

public class InventoryBlastFurnace(
  int quantitySlots,
  string className,
  string? instanceID,
  ICoreAPI? api
) : InventoryGeneric(quantitySlots, className, instanceID, api) {
  protected override ItemSlot NewSlot(int i) {
    if (i == 0 || i == 1 || i == 4 || i == 5)
      return new ItemSlotBlastFurnace(this, "iron");
    if (i == 2 || i == 6)
      return new ItemSlotBlastFurnace(this, "coke");
    if (i == 3 || i == 7)
      return new ItemSlotBlastFurnace(this, "lime");

    return base.NewSlot(i);
  }
}

public class ItemSlotBlastFurnace : ItemSlotSurvival {
  public string AllowedType { get; }

  public ItemSlotBlastFurnace(InventoryBase inventory, string allowedType)
    : base(inventory) {
    AllowedType = allowedType;

    // The engine automatically handles rendering these hex colors!
    HexBackgroundColor = allowedType switch {
      "iron" => "#A05A3C", // Rust Orange
      "coke" => "#222222", // Dark Charcoal
      "lime" => "#78A278", // Pale Green
      _ => null,
    };
  }

  public override bool CanTakeFrom(
    ItemSlot sourceSlot,
    EnumMergePriority priority = EnumMergePriority.AutoMerge
  ) {
    if (sourceSlot.Itemstack == null)
      return base.CanTakeFrom(sourceSlot, priority);

    string path = sourceSlot.Itemstack.Collectible.Code.Path;

    // Iron slots also accept reclaimed blastmix (e.g. from broken-up piles), so
    // it can be fed straight back into the bell hopper's magazine.
    if (
      AllowedType == "iron"
      && (IronOreCompat.IsCrushedIronOre(path) || path.Equals("blastmix"))
    )
      return base.CanTakeFrom(sourceSlot, priority);
    // The fuel slot takes coke or charcoal; the bell hopper below exchanges them by carbon
    // content, so a batch can be fed either or a mix of both.
    if (
      AllowedType == "coke"
      && (path.Equals("coke") || path.Equals("charcoal"))
    )
      return base.CanTakeFrom(sourceSlot, priority);
    if (AllowedType == "lime" && path.Equals("lime"))
      return base.CanTakeFrom(sourceSlot, priority);

    return false;
  }

  public override bool CanHold(ItemSlot sourceSlot) => CanTakeFrom(sourceSlot);
}

public class GuiDialogHopper : GuiDialogBlockEntity {
  public GuiDialogHopper(
    string dialogTitle,
    InventoryBase inventory,
    BlockPos blockEntityPos,
    ICoreClientAPI capi
  )
    : base(dialogTitle, inventory, blockEntityPos, capi) {
    if (IsDuplicate)
      return;

    double colWidth =
      GuiElementPassiveItemSlot.unscaledSlotSize
      + GuiElementItemSlotGridBase.unscaledSlotPadding;

    ElementBounds ironTextBounds = ElementBounds.Fixed(
      0,
      GuiStyle.TitleBarHeight,
      colWidth * 2,
      15
    );
    ElementBounds cokeTextBounds = ElementBounds.Fixed(
      colWidth * 2,
      GuiStyle.TitleBarHeight,
      colWidth,
      15
    );
    ElementBounds limeTextBounds = ElementBounds.Fixed(
      colWidth * 3,
      GuiStyle.TitleBarHeight,
      colWidth,
      15
    );

    ElementBounds slotGridBounds = ElementStdBounds.SlotGrid(
      EnumDialogArea.LeftTop,
      0,
      GuiStyle.TitleBarHeight + 25,
      4,
      2
    );

    ElementBounds bgBounds = ElementBounds.Fill.WithFixedPadding(
      GuiStyle.ElementToDialogPadding
    );
    bgBounds.BothSizing = ElementSizing.FitToChildren;

    ElementBounds dialogBounds =
      ElementStdBounds.AutosizedMainDialog.WithAlignment(
        EnumDialogArea.CenterMiddle
      );

    var ironFont = CairoFont
      .WhiteSmallText()
      .WithColor([0.8, 0.5, 0.4, 1])
      .WithOrientation(EnumTextOrientation.Center);
    var cokeFont = CairoFont
      .WhiteSmallText()
      .WithColor([0.7, 0.7, 0.7, 1])
      .WithOrientation(EnumTextOrientation.Center);
    var limeFont = CairoFont
      .WhiteSmallText()
      .WithColor([0.8, 0.9, 0.8, 1])
      .WithOrientation(EnumTextOrientation.Center);
    var infoFont = CairoFont.WhiteSmallText().WithFontSize(14f);

    SingleComposer = capi
      .Gui.CreateCompo("hopper" + blockEntityPos, dialogBounds)
      .AddShadedDialogBG(bgBounds, true)
      .AddDialogTitleBar(dialogTitle, CloseIconPressed)
      .BeginChildElements(bgBounds)
      .AddDynamicText(
        Lang.Get("smex:hopper-slot-iron"),
        ironFont,
        ironTextBounds
      )
      .AddDynamicText(
        Lang.Get("smex:hopper-slot-coke"),
        cokeFont,
        cokeTextBounds
      )
      .AddDynamicText(
        Lang.Get("smex:hopper-slot-flux"),
        limeFont,
        limeTextBounds
      )
      .AddItemSlotGrid(inventory, DoSendPacket, 4, slotGridBounds)
      .EndChildElements()
      .Compose();
  }
}

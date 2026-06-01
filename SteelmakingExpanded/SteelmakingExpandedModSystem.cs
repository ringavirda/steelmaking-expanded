using System;
using System.Linq;
using System.Reflection;
using BlockNetworkLib;
using SteelmakingExpanded.Networks.Gas;
using SteelmakingExpanded.Networks.Gas.BlockEntities;
using SteelmakingExpanded.Networks.Gas.Blocks;
using SteelmakingExpanded.Networks.Molten;
using SteelmakingExpanded.Networks.Molten.BlockEntities;
using SteelmakingExpanded.Networks.Molten.Blocks;
using SteelmakingExpanded.Overrides;
using SteelmakingExpanded.Structures.BessemerConverter.BlockEntities;
using SteelmakingExpanded.Structures.BessemerConverter.Blocks;
using SteelmakingExpanded.Structures.BlastFurnace.BlockEntities;
using SteelmakingExpanded.Structures.BlastFurnace.Blocks;
using SteelmakingExpanded.Structures.BlastFurnace.Items;
using SteelmakingExpanded.Structures.CowperStove.BlockEntities;
using SteelmakingExpanded.Structures.CowperStove.Blocks;
using SteelmakingExpanded.Structures.SmokeStack.BlockEntities;
using SteelmakingExpanded.Structures.SmokeStack.Blocks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SteelmakingExpanded;

/// <summary>
/// Main mod system for Steelmaking Expanded. Registers all block, block-entity, item
/// and behavior classes; adds the mod's creative tab; wires up global player-side
/// effects (molten-mold burns and spills); registers the gas and molten network
/// types; and patches a few vanilla collectibles (coke crushing).
/// </summary>
public class SteelmakingExpandedModSystem : ModSystem
{
  #region Creative category
  public override void StartClientSide(ICoreClientAPI api)
  {
    var creativeCustomTab = Mod.Info.ModID;
    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      Type? type = assembly.GetType(
        "Vintagestory.Client.NoObf.GuiDialogCreativeTabs"
      );
      if (type != null)
      {
        FieldInfo? field = type.GetField(
          "tabs",
          BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic
        );
        if (field != null)
        {
          var currentTabs = (string[]?)field.GetValue(null);

          if (
            currentTabs == null
            || Array.IndexOf(currentTabs, creativeCustomTab) == -1
          )
          {
            var newTabs = currentTabs?.Append(creativeCustomTab);
            field.SetValue(null, newTabs);
          }
        }
        break;
      }
    }
  }
  #endregion

  #region Global player interactions
  private const float MoldMinBurnTemperature = 200f;

  public override void StartServerSide(ICoreServerAPI api)
  {
    api.Event.AfterActiveSlotChanged += (player, ev) =>
      OnAfterActiveSlotChanged(api, player, ev);
    api.Event.RegisterGameTickListener(_ => OnMoldServerTick(api), 1000);
  }

  private static void OnMoldServerTick(ICoreServerAPI api)
  {
    foreach (var p in api.World.AllOnlinePlayers)
    {
      if (p is not IServerPlayer player || player.Entity?.Alive != true)
        continue;

      var invManager = player.InventoryManager;
      if (invManager == null)
        continue;

      ItemSlot? activeSlot = invManager.ActiveHotbarSlot;

      BurnIfHoldingHotMold(api, player, activeSlot);

      foreach (var inv in invManager.InventoriesOrdered)
        SpillMoltenMolds(inv, activeSlot, api, player);

      foreach (var inv in invManager.OpenedInventories)
        SpillMoltenMolds(inv, activeSlot, api, player);
    }
  }

  private static void SpillMoltenMolds(
    IInventory inv,
    ItemSlot? activeSlot,
    ICoreServerAPI api,
    IServerPlayer player
  )
  {
    if (inv == null || inv.ClassName == GlobalConstants.creativeInvClassName)
      return;

    foreach (var slot in inv)
      if (slot != activeSlot)
        MoltenMoldSpill.SpillIfMolten(slot, api.World, player);
  }

  private static void BurnIfHoldingHotMold(
    ICoreServerAPI api,
    IServerPlayer player,
    ItemSlot? activeSlot
  )
  {
    var stack = activeSlot?.Itemstack;
    if (stack?.Block is not BlockToolMold)
      return;

    var beData = stack.Attributes?.GetTreeAttribute("blockEntityAttributes");
    var contents = beData?.GetItemstack("contents");
    int fill = beData?.GetInt("fillLevel") ?? 0;
    if (contents == null || fill <= 0)
      return;

    contents.ResolveBlockOrItem(api.World);
    if (contents.Collectible == null)
      return;

    float temp = contents.Collectible.GetTemperature(api.World, contents);
    if (temp < MoldMinBurnTemperature || HasHandProtection(player))
      return;

    player.Entity.ReceiveDamage(
      new DamageSource
      {
        Source = EnumDamageSource.Block,
        Type = EnumDamageType.Fire,
      },
      1f
    );
  }

  private static bool HasHandProtection(IServerPlayer player)
  {
    var charInv = player.InventoryManager?.GetOwnInventory(
      GlobalConstants.characterInvClassName
    );
    int handSlot = (int)EnumCharacterDressType.Hand;
    if (charInv == null || handSlot >= charInv.Count)
      return false;

    string? path = charInv[handSlot]?.Itemstack?.Collectible?.Code?.Path;
    return path
      is "clothes-hand-heavy-leather-gloves"
        or "clothes-nadiya-hand-blacksmith";
  }

  private static void OnAfterActiveSlotChanged(
    ICoreServerAPI api,
    IServerPlayer player,
    ActiveSlotChangeEventArgs ev
  )
  {
    var hotbar = player.InventoryManager?.GetHotbarInventory();
    if (hotbar == null || ev.FromSlot < 0 || ev.FromSlot >= hotbar.Count)
      return;

    MoltenMoldSpill.SpillIfMolten(hotbar[ev.FromSlot], api.World, player);
  }
  #endregion

  #region Entity register/override
  public override void Start(ICoreAPI api)
  {
    // Blocks
    RegisterBlock<BlockBlastFurnaceDoor>(api);
    RegisterBlock<BlockHopperReinforced>(api);
    RegisterBlock<BlockHopperBell>(api);
    RegisterBlock<BlockTuyere>(api);
    RegisterBlock<BlockBlastFurnaceTap>(api);
    RegisterBlock<BlockSlag>(api);
    RegisterBlock<BlockSolidifiedIron>(api);

    RegisterBlock<BlockBessemerConverter>(api);
    RegisterBlock<BlockBessemerGasIntake>(api);
    RegisterBlock<BlockBessemerControl>(api);
    RegisterBlock<BlockBessemerTransmission>(api);

    RegisterBlock<BlockGasPipe>(api);
    RegisterBlock<BlockGasPassthrough>(api);
    RegisterBlock<BlockGasOutlet>(api);
    RegisterBlock<BlockGasValve>(api);
    RegisterBlock<BlockGasPressureValve>(api);
    RegisterBlock<BlockGasHeatedIntake>(api);
    RegisterBlock<BlockGasBlower>(api);
    RegisterBlock<BlockGasIntake>(api);

    RegisterBlock<BlockSmokeStackIntake>(api);
    RegisterBlock<BlockCowperStoveIntake>(api);
    RegisterBlock<BlockHeatSink>(api);

    RegisterBlock<BlockMoltenCanal>(api);
    RegisterBlock<BlockMoltenCanalStart>(api);
    RegisterBlock<BlockMoltenCanalTap>(api);
    RegisterBlock<BlockMoltenCanalMoldPedestal>(api);
    RegisterBlock<BlockMoltenBarrel>(api);

    // Entities
    RegisterBlockEntity<BlockEntityBlastFurnace>(api);
    RegisterBlockEntity<BlockEntityTuyere>(api);
    RegisterBlockEntity<BlockEntityHopperReinforced>(api);
    RegisterBlockEntity<BlockEntityHopperBell>(api);
    RegisterBlockEntity<BlockEntityBlastFurnaceTap>(api);
    RegisterBlockEntity<BlockEntitySlag>(api);
    RegisterBlockEntity<BlockEntitySolidifiedIron>(api);

    RegisterBlockEntity<BlockEntityBessemerConverter>(api);
    RegisterBlockEntity<BlockEntityBessemerGasIntake>(api);
    RegisterBlockEntity<BlockEntityBessemerControl>(api);
    RegisterBlockEntity<BlockEntityBessemerTransmission>(api);

    RegisterBlockEntity<BlockEntityCowperStove>(api);
    RegisterBlockEntity<BlockEntityHeatSink>(api);

    RegisterBlockEntity<BlockEntityGasPipe>(api);
    RegisterBlockEntity<BlockEntityGasPassthrough>(api);
    RegisterBlockEntity<BlockEntityGasOutlet>(api);
    RegisterBlockEntity<BlockEntityGasValve>(api);
    RegisterBlockEntity<BlockEntityGasPressureValve>(api);
    RegisterBlockEntity<BlockEntityGasHeatedIntake>(api);
    RegisterBlockEntity<BlockEntityGasBlower>(api);
    RegisterBlockEntity<BlockEntityGasIntake>(api);

    RegisterBlockEntity<BlockEntitySmokeStack>(api);

    RegisterBlockEntity<BlockEntityMoltenCanal>(api);
    RegisterBlockEntity<BlockEntityMoltenCanalStart>(api);
    RegisterBlockEntity<BlockEntityMoltenCanalTap>(api);
    RegisterBlockEntity<BlockEntityMoltenCanalMoldPedestal>(api);
    RegisterBlockEntity<BlockEntityMoltenBarrel>(api);

    // Items
    RegisterItem<ItemBlastmix>(api);

    // Behaviors
    RegisterEntityBehavior<BEBehaviorMPBlower>(api);
    RegisterEntityBehavior<BEBehaviorMPBessemerTransmission>(api);

    // Override vanilla CoalPile to globally inject Blast Mix burn logic
    api.RegisterBlockEntityClass("CoalPile", typeof(CustomBlockEntityCoalPile));

    // Override vanilla ToolMold so a filled mold handed back by the pedestal
    // restores its contents when placed in the world (vanilla ignores the
    // stored blockEntityAttributes on placement).
    api.RegisterBlockEntityClass("ToolMold", typeof(CustomBlockEntityToolMold));

    // Override vanilla BlockToolMold to refine the right-click pickup flow:
    // extract the cast item first, then pick the mold up (allowed even before
    // it hardens, carrying any remaining contents).
    api.RegisterBlockClass("BlockToolMold", typeof(CustomBlockToolMold));

    // Override vanilla BlockMoldRack so a molten mold spills when racked.
    api.RegisterBlockClass("BlockMoldRack", typeof(CustomBlockMoldRack));

    // Register typed network factories — each creates the correct BlockNetwork subclass.
    var netManager = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    netManager.RegisterNetworkType("gas", () => new GasNetwork(netManager));
    netManager.RegisterNetworkType(
      "molten",
      () => new MoltenNetwork(netManager)
    );
  }

  public override void AssetsFinalize(ICoreAPI api)
  {
    base.AssetsFinalize(api);

    Item? cokeItem = api.World.GetItem(new AssetLocation("game", "coke"));
    Item? crushedCokeItem = api.World.GetItem(
      new AssetLocation("game", "crushed-coke")
    );

    if (cokeItem != null && crushedCokeItem != null)
    {
      var outputStack = new JsonItemStack()
      {
        Code = crushedCokeItem.Code,
        Type = EnumItemClass.Item,
      };

      outputStack.Resolve(api.World, "steelmaking-coke-override");

      cokeItem.CrushingProps = new CrushingProperties()
      {
        CrushedStack = outputStack,
        Quantity = NatFloat.createUniform(2, 0),
        HardnessTier = 1,
      };
    }
  }
  #endregion

  #region Helper methods
  private void RegisterBlock<T>(ICoreAPI api)
    where T : Block
  {
    var className = typeof(T).Name;
    var fullBlockId = PrefixModId(className);
    api.RegisterBlockClass(fullBlockId, typeof(T));
  }

  private void RegisterBlockEntity<T>(ICoreAPI api)
    where T : BlockEntity
  {
    var blockId = typeof(T).Name;
    var fullBlockId = PrefixModId(blockId);
    api.RegisterBlockEntityClass(fullBlockId, typeof(T));

    if (blockId.StartsWith("BlockEntity"))
    {
      var shortId = blockId.Substring(11);
      api.RegisterBlockEntityClass(PrefixModId(shortId), typeof(T));
      api.RegisterBlockEntityClass(shortId, typeof(T));
      api.RegisterBlockEntityClass(shortId.ToLowerInvariant(), typeof(T));
    }
  }

  private void RegisterItem<T>(ICoreAPI api)
    where T : Item
  {
    var itemId = typeof(T).Name;
    var fullItemId = PrefixModId(itemId);
    api.RegisterItemClass(fullItemId, typeof(T));
  }

  private void RegisterEntityBehavior<T>(ICoreAPI api)
    where T : BlockEntityBehavior
  {
    var className = typeof(T).Name;
    var fullClassName = PrefixModId(className);
    api.RegisterBlockEntityBehaviorClass(fullClassName, typeof(T));
  }

  private string PrefixModId(string entityId) => $"{Mod.Info.ModID}.{entityId}";
  #endregion
}

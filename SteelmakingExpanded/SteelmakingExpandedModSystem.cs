using System;
using System.Linq;
using System.Reflection;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using SteelmakingExpanded.Compat;
using SteelmakingExpanded.Overrides;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SteelmakingExpanded;

/// <summary>
/// Main mod system for Steelmaking Expanded. Auto-registers every block, block-entity, item
/// and behavior class via <see cref="EntityRegistry"/>; adds the mod's creative tab; wires up
/// global player-side effects (molten-mold burns and spills); registers the molten network
/// type; and patches a few vanilla collectibles (coke crushing). The pipe network and all
/// pipe/steam-power content now live in the Pipes and Power Expanded mod (ppex).
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
    if (temp < SmexValues.MoldBurnMinTemperature || HasHandProtection(player))
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

  #region Registration
  public override void Start(ICoreAPI api)
  {
    // Load gameplay tunables from ModConfig/smex.json (writes defaults on first
    // run). Done before any block entity is constructed so the values apply.
    SmexValues.Load(api);

    // Register other mod's iron ore types.
    IronOreCompat.Init(api);

    // The shared structure-filler block lives in exlib (a hard dependency); exlib points the
    // StructureFillers helper at exlib:structurefiller, which this mod's mega-blocks reuse.

    // Auto-register every [EntityRegister] block / block entity / item / behavior
    // (and the vanilla-class overrides) declared in this assembly.
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The molten-metal network. The unified "pipe" network is registered by ppex.
    var netManager = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
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
}

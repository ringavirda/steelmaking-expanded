using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using HarmonyLib;
using SteelmakingExpanded.BlockNetworkMolten;
using SteelmakingExpanded.BlockNetworkMolten.Blocks;
using SteelmakingExpanded.Compat;
using SteelmakingExpanded.Patches;
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
/// type; applies the Harmony patches that extend the vanilla tool mold / mold rack / coal
/// pile; and patches a few vanilla collectibles (coke crushing). The pipe network and all
/// pipe/steam-power content now live in the Pipes and Power Expanded mod (ppex).
/// </summary>
public class SteelmakingExpandedModSystem : ModSystem
{
  private Harmony? _harmony;

  public override void Dispose()
  {
    ToolMoldPatches.ClearMeshCache();
    _harmony?.UnpatchAll(Mod.Info.ModID);
    _harmony = null;
    base.Dispose();
  }

  #region Creative category
  public override void StartClientSide(ICoreClientAPI api) =>
    ExCreativeTabs.EnsureTab(Mod.Info.ModID);
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

    var (contents, fill) = MoltenContents.Read(
      stack,
      MoltenContents.MoldUnitsKey,
      api.World
    );
    if (contents?.Collectible == null || fill <= 0)
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

    // Harmony patches that extend the vanilla tool mold, mold rack and coal pile
    // (filled-mold handling and blast-mix burn-to-slag) without replacing their
    // registered classes, so other mods touching those blocks can coexist.
    if (!Harmony.HasAnyPatches(Mod.Info.ModID))
    {
      _harmony = new Harmony(Mod.Info.ModID);
      _harmony.PatchAll(GetType().Assembly);
    }

    // The shared structure-filler block lives in exlib (a hard dependency); exlib points the
    // StructureFillers helper at exlib:structurefiller, which this mod's mega-blocks reuse.

    // Auto-register every [EntityRegister] block / block entity / item / behavior
    // declared in this assembly.
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The molten-metal network. The unified "pipe" network is registered by ppex.
    var netManager = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    netManager.RegisterNetworkType(
      "molten",
      () => new MoltenNetwork(netManager)
    );
  }

  #endregion
}

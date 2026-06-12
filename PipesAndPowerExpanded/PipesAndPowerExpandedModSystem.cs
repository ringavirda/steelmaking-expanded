using ExpandedLib;
using ExpandedLib.BlockNetworks;
using ExpandedLib.EntityRegistry;
using HarmonyLib;
using PipesAndPowerExpanded.BlockNetworkPipe;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PipesAndPowerExpanded;

/// <summary>
/// Main mod system for Pipes and Power Expanded. Loads the gameplay tunables, points the
/// shared structure-filler at this mod's own filler block, auto-registers every
/// <see cref="EntityRegisterAttribute"/>-decorated class, adds the creative tab, and
/// registers the unified "pipe" network type that carries both gases and liquids.
/// </summary>
public class PipesAndPowerExpandedModSystem : ModSystem
{
  private Harmony? _harmony;

  public override void Start(ICoreAPI api)
  {
    // Load gameplay tunables from ModConfig/ppex.json (writes defaults on first run).
    PpexValues.Load(api);

    // Patch the vanilla chimney's look-at info so a chimney venting one of our pipes
    // reports it (the gas draw itself runs in PipeNetwork's tick).
    if (!Harmony.HasAnyPatches(Mod.Info.ModID))
    {
      _harmony = new Harmony(Mod.Info.ModID);
      _harmony.PatchAll(GetType().Assembly);
    }

    // The shared structure-filler block and network/structure framework live in the exlib
    // mod (a hard dependency); exlib points StructureFillers at exlib:structurefiller and
    // registers its own classes. Here we only register ppex's own content.
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The unified pipe network (gas + liquid pools).
    var netManager = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    netManager.RegisterNetworkType("pipe", () => new PipeNetwork(netManager));
  }

  public override void Dispose()
  {
    _harmony?.UnpatchAll(Mod.Info.ModID);
    _harmony = null;
    base.Dispose();
  }

  #region Creative category
  public override void StartClientSide(ICoreClientAPI api) =>
    ExCreativeTabs.EnsureTab(Mod.Info.ModID);
  #endregion
}

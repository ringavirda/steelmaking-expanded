using System;
using System.Linq;
using System.Reflection;
using ExpandedLib.BlockNetworks;
using ExpandedLib.BlockStructures;
using ExpandedLib.EntityRegistry;
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
  public override void Start(ICoreAPI api)
  {
    // Load gameplay tunables from ModConfig/ppex.json (writes defaults on first run).
    PpexValues.Load(api);

    // ExpandedLib compiles into this mod; ppex owns the shared structure-filler block, which
    // dependent mods (e.g. smex) reuse for their own mega-blocks.
    StructureFillers.FillerCode = new AssetLocation(
      Mod.Info.ModID,
      "structurefiller"
    );

    // Auto-register all blocks / block entities / items / behaviours in this assembly.
    EntityRegistry.RegisterAll(api, Mod, GetType().Assembly);

    // The unified pipe network (gas + liquid pools).
    var netManager = api.ModLoader.GetModSystem<BlockNetworkModSystem>();
    netManager.RegisterNetworkType("pipe", () => new PipeNetwork(netManager));
  }

  #region Creative category
  public override void StartClientSide(ICoreClientAPI api)
  {
    var creativeCustomTab = Mod.Info.ModID;
    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
    {
      Type? type = assembly.GetType(
        "Vintagestory.Client.NoObf.GuiDialogCreativeTabs"
      );
      if (type == null)
        continue;

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
          field.SetValue(
            null,
            currentTabs?.Append(creativeCustomTab).ToArray()
          );
      }
      break;
    }
  }
  #endregion
}

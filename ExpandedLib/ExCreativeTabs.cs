using System;
using System.Linq;
using System.Reflection;

namespace ExpandedLib;

/// <summary>
/// Registers a mod's custom creative-inventory tab with the (internal) vanilla tab list.
/// The tab list lives on the non-public <c>GuiDialogCreativeTabs.tabs</c> field, so both
/// dependent mods previously carried their own copy of this reflection — it now lives
/// here once. Call from <c>ModSystem.StartClientSide</c>.
/// </summary>
public static class ExCreativeTabs
{
  /// <summary>
  /// Appends <paramref name="tabCode"/> (typically the mod id, matching the blocks'
  /// <c>creativeinventory</c> JSON key) to the client's creative tab list, unless it is
  /// already present. No-op when the internal type/field cannot be found.
  /// </summary>
  public static void EnsureTab(string tabCode)
  {
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
        if (currentTabs == null || Array.IndexOf(currentTabs, tabCode) == -1)
          field.SetValue(null, currentTabs?.Append(tabCode).ToArray());
      }
      break;
    }
  }
}

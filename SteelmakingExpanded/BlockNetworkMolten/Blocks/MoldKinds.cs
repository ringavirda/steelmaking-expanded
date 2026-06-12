using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace SteelmakingExpanded.BlockNetworkMolten.Blocks;

/// <summary>
/// Classifies tool molds by where they can be cast: small molds sit on the
/// mold pedestal, large molds (anvil, helve hammer) are cast in the canal tap.
/// </summary>
public static class MoldKinds
{
  /// <summary>Tool-mold types too large for the pedestal; cast in the canal tap instead.</summary>
  public static readonly HashSet<string> LargeToolTypes =
  [
    "helvehammer",
    "anvil",
  ];

  /// <summary>Returns <c>true</c> when <paramref name="block"/> is a large tool mold (tap-only).</summary>
  public static bool IsLarge(Block? block) =>
    block is BlockToolMold
    && LargeToolTypes.Contains(block.Variant["tooltype"] ?? "");

  /// <summary>Returns <c>true</c> when <paramref name="block"/> is a small tool mold that fits the pedestal.</summary>
  public static bool FitsPedestal(Block? block) =>
    block is BlockToolMold
    && !LargeToolTypes.Contains(block.Variant["tooltype"] ?? "");
}

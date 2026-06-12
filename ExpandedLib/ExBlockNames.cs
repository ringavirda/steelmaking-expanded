using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib;

/// <summary>
/// Composes block display names that include the block's material-ish variant —
/// pipe metal ("Iron Piping"), canal rock ("Granite Molten Canal"), passthrough
/// brick ("Fire Brick Pipe Passthrough") and refractory tier ("Smoke Stack Intake
/// (Refractory Tier 3)") — so same-shaped blocks of different materials are
/// distinguishable in the inventory, handbook and look-at HUD.
/// </summary>
public static class ExBlockNames
{
  /// <summary>
  /// Decorates <paramref name="baseName"/> with the recognised variant values of
  /// <paramref name="block"/>. Metal materials and rocks resolve through the
  /// vanilla <c>material-*</c> / <c>rock-*</c> lang keys; brick variants resolve
  /// through <c>{domain}:brickname-*</c> keys shipped by the block's own mod.
  /// Blocks without any of these variants are returned unchanged.
  /// </summary>
  public static string Decorate(Block block, string baseName)
  {
    string name = baseName;

    string? material = block.Variant["material"];
    string? rock = block.Variant["rock"];
    string? brick = block.Variant["brick"];

    if (material != null)
      name = Lang.Get(
        "exlib:blockname-prefixed",
        Lang.Get("material-" + material),
        name
      );
    else if (rock != null)
      name = Lang.Get(
        "exlib:blockname-prefixed",
        Lang.Get("rock-" + rock),
        name
      );
    else if (brick != null)
      name = Lang.Get(
        "exlib:blockname-prefixed",
        Lang.Get(block.Code.Domain + ":brickname-" + brick),
        name
      );

    // Refractory tier is its own variant group (cowper stove / smoke stack
    // intakes), appended as a suffix so it can combine with the prefixes above.
    string? refractory = block.Variant["refractory"];
    if (refractory != null)
      name = Lang.Get(
        "exlib:blockname-suffixed",
        name,
        Lang.Get("exlib:refractory-" + refractory)
      );

    return name;
  }
}

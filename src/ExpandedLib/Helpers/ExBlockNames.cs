using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Helpers;

/// <summary>
/// Composes block display names that include the block's material-ish variant -
/// pipe metal ("Piping (Straight, Steel)"), canal rock ("Molten Canal (Bend, Granite)"),
/// passthrough brick ("Pipe Passthrough (Straight, Fire Brick)") and refractory tier
/// ("Smoke Stack Intake (Refractory Tier 3)") - so same-shaped blocks of different
/// materials are distinguishable in the inventory, handbook and look-at HUD.
/// <para>
/// The qualifier is always appended as a parenthetical <em>suffix</em>, following vanilla's
/// single coherent block name (e.g. "Стальная труба"). A leading noun prefix
/// ("сталь Труба") does not decline to agree with the noun in Russian/Ukrainian and reads
/// incoherently, so we never prefix; instead each qualifier is folded into the name's
/// trailing "(…)" group when it already has one.
/// </para>
/// </summary>
public static class ExBlockNames {
  /// <summary>
  /// Decorates <paramref name="baseName"/> with the recognised variant values of
  /// <paramref name="block"/>. Metal materials and rocks resolve through the
  /// vanilla <c>material-*</c> / <c>rock-*</c> lang keys; brick variants resolve
  /// through <c>{domain}:brickname-*</c> keys shipped by the block's own mod.
  /// Blocks without any of these variants are returned unchanged.
  /// </summary>
  public static string Decorate(Block block, string baseName) {
    string name = baseName;

    string? material = block.Variant["material"];
    string? rock = block.Variant["rock"];
    string? brick = block.Variant["brick"];

    if (material != null)
      name = AppendQualifier(name, Lang.Get("material-" + material));
    else if (rock != null)
      name = AppendQualifier(name, Lang.Get("rock-" + rock));
    else if (brick != null)
      name = AppendQualifier(
        name,
        Lang.Get(block.Code.Domain + ":brickname-" + brick)
      );

    // Refractory tier is its own variant group (cowper stove / smoke stack intakes).
    string? refractory = block.Variant["refractory"];
    if (refractory != null)
      name = AppendQualifier(name, Lang.Get("exlib:refractory-" + refractory));

    return name;
  }

  /// <summary>
  /// Appends <paramref name="qualifier"/> to <paramref name="name"/> as a parenthetical
  /// suffix. If the name already ends with a "(…)" group (a shape qualifier such as
  /// "Piping (Straight)"), the qualifier is merged into that group
  /// ("Piping (Straight, Steel)") so brackets never stack.
  /// </summary>
  private static string AppendQualifier(string name, string qualifier) {
    if (name.EndsWith(')') && name.Contains('('))
      return name[..^1] + Lang.Get("exlib:blockname-listsep") + qualifier + ")";
    return Lang.Get("exlib:blockname-suffixed", name, qualifier);
  }
}

using System.Collections.Generic;
using ExpandedLib.BlockMigrations;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace SteelmakingExpanded.BlockMigrations;

/// <summary>
/// Migrates the three add-on tool molds — plate, double ingot and quad rod — from the
/// vanilla <c>game:</c> domain to <c>smex:</c>. They used to be patched into vanilla's
/// <c>game:toolmold</c> blocktype; they are now standalone <c>smex:toolmold-*</c> blocks
/// (see <c>assets/smex/blocktypes/molds/</c>), so previously placed or stored molds load
/// with the now-removed <c>game:</c> code and must be rewritten.
///
/// <para>The code structure is identical across the move (<c>toolmold-{color}-{material}-{tool}</c>),
/// only the domain changes. The fired molds carry a block entity (the molten charge, fill
/// level, mesh angle), so this also implements <see cref="IBlockEntityMigration"/> to copy
/// that state verbatim onto the replacement — both the new and old block entities are the
/// vanilla <c>ToolMold</c> class, so the tree maps one-to-one. Empty and raw molds simply
/// swap with no state to carry.</para>
///
/// <para>This deliberately uses the mod's migration framework rather than a vanilla
/// <c>config/remaps.json</c> remap: the vanilla auto-remapper rewrites the savegame's
/// block-code↔id table in place, but its <c>AutoRemap</c> does not remove the duplicate it
/// creates when the target code is itself a freshly registered block (which <c>smex:toolmold-*</c>
/// is). That left orphaned block ids and null-block tool-mold entities. The framework instead
/// swaps blocks in-world after they have fully resolved, and the item-stack pass below also
/// catches molds sitting in inventories and ground storage.</para>
/// </summary>
public class ToolMoldDomainMigration : IBlockCodeMigration, IBlockEntityMigration
{
  public string Name => "Plate/double-ingot/quad-rod molds moved to smex domain";

  // Colours each material type ships with, mirroring the smex blocktype variantgroups:
  // raw molds clay-form in three colours; firing (kiln/burning) can yield any of ten.
  private static readonly string[] RawColors = ["blue", "red", "fire"];
  private static readonly string[] FiredColors =
  [
    "blue",
    "fire",
    "black",
    "brown",
    "cream",
    "earthyorange",
    "gray",
    "orange",
    "red",
    "tan",
  ];
  private static readonly string[] ToolTypes = ["plate", "doubleingot", "quadrod"];

  public IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  )
  {
    foreach (var (material, colors) in new[]
    {
      ("raw", RawColors),
      ("fired", FiredColors),
    })
    foreach (string color in colors)
    foreach (string tool in ToolTypes)
    {
      string path = $"toolmold-{color}-{material}-{tool}";
      yield return (new AssetLocation("game", path), new AssetLocation("smex", path));
    }
  }

  /// <summary>
  /// Copies the old tool mold's serialized state (molten contents, fill level, shatter
  /// state, mesh angle) onto the replacement. Both sides are the vanilla <c>ToolMold</c>
  /// block entity, so a verbatim apply is correct; it also resolves the metal stack and
  /// refreshes the renderer.
  /// </summary>
  public void MigrateBlockEntity(
    AssetLocation oldCode,
    AssetLocation newCode,
    ITreeAttribute? oldState,
    BlockEntity newBlockEntity,
    IWorldAccessor world
  )
  {
    if (oldState != null)
      newBlockEntity.FromTreeAttributes(oldState, world);
  }
}

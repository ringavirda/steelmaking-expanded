using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.Blocks.Migrations;

/// <summary>
/// The item counterpart of <see cref="IBlockCodeMigration"/>: declares how item codes from an older
/// version of the mod should be rewritten. Implement this (with a public parameterless constructor)
/// when an item is renamed or retired, and <see cref="BlockMigrationModSystem"/> rewrites the stacks
/// it finds in block-entity inventories as chunks load and in a player's own inventories on join.
/// <para>
/// Items and blocks are remapped through separate tables even though both are keyed by
/// <see cref="AssetLocation"/>, because an item and a block may legitimately share one code. Mixing
/// them would let a block migration rewrite a same-named item stack into a block - or, since a block
/// table can never resolve an item code, silently delete it as if it were a purge.
/// </para>
/// </summary>
public interface IItemCodeMigration
{
  /// <summary>Short human-readable name, used only for log output.</summary>
  string Name { get; }

  /// <summary>
  /// Returns <c>(oldCode, newCode)</c> pairs of full, domain-qualified item codes. Pairs whose old
  /// or new code is absent in this world are skipped, so it is safe to return the full set
  /// unconditionally. Stack size and attributes carry over unchanged, so a mapping that is not
  /// one-for-one in value is a balance decision the caller has already made.
  /// </summary>
  IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  );
}

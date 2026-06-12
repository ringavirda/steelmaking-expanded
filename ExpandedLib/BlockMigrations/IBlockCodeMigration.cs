using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace ExpandedLib.BlockMigrations;

/// <summary>
/// Declares how block codes from an older version of the mod should be rewritten to
/// their current equivalents. Implement this (with a public parameterless constructor)
/// whenever you rename or re-variant a block: <see cref="BlockMigrationModSystem"/>
/// auto-discovers every implementation and rewrites previously placed instances — which
/// load as "missing" placeholder blocks that keep their original code — in-world as
/// chunks load.
/// </summary>
public interface IBlockCodeMigration
{
  /// <summary>Short human-readable name, used only for log output.</summary>
  string Name { get; }

  /// <summary>
  /// Returns <c>(oldCode, newCode)</c> pairs of full, domain-qualified block codes.
  /// <paramref name="api"/> is provided so implementations can enumerate variants
  /// programmatically. Old codes are the ones that no longer resolve; each new code
  /// must be a currently registered block. Pairs whose old or new code is absent in
  /// this world are skipped, so it is safe to return the full set unconditionally.
  /// </summary>
  IEnumerable<(AssetLocation oldCode, AssetLocation newCode)> GetRemaps(
    ICoreServerAPI api
  );
}

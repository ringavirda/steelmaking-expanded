namespace ExpandedLib.Registries.Config;

/// <summary>
/// Declares that upgrading a mod into (or past) <see cref="ToVersion"/> resets the named config
/// properties back to their coded defaults, discarding the player's saved tuning for just those
/// keys. Every value not listed is preserved across the upgrade.
/// <para>
/// Migrations are handed to the mod's <see cref="ExConfigRegister{TConfig}"/> at construction. On load
/// the store compares the version stamped in the config file (the "from" version) to the running mod
/// version and applies every migration whose <see cref="ToVersion"/> falls in that gap, oldest
/// first. A migration fires <em>once</em> - the first time a build at or past its
/// <see cref="ToVersion"/> loads a file last saved below it.
/// </para>
/// <para>
/// To target a single transition (e.g. "0.9.1 =&gt; 0.9.2"), set <see cref="FromVersion"/> to the
/// lower bound: the reset then only fires for files saved at or above it. Leave it null to reset on
/// any upgrade that crosses <see cref="ToVersion"/>, regardless of how old the file is.
/// </para>
/// </summary>
public sealed class ExConfigMigration {
  /// <summary>The mod version this reset is tied to (e.g. <c>"0.9.2"</c>). The reset fires when the
  /// player first runs a build at or past this version having last saved below it.</summary>
  public required string ToVersion { get; init; }

  /// <summary>Optional lower bound: only fire when the file's stamped version is at or above this
  /// (e.g. <c>"0.9.1"</c> to scope the reset to the exact "0.9.1 =&gt; 0.9.2" step). Null = fire for
  /// any older version, including pre-versioning files (which have no stamp).</summary>
  public string? FromVersion { get; init; }

  /// <summary>Config property names to reset to their defaults - use <c>nameof</c> so renames stay in
  /// sync (e.g. <c>[nameof(PpexConfig.LitresPerPipe)]</c>). Leave null or empty to reset the whole
  /// config to defaults.</summary>
  public string[]? ResetFields { get; init; }
}

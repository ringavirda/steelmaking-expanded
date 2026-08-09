namespace ExpandedLib.Registries.Config;

/// <summary>
/// A JSON config POCO that records the mod version it was last written under. The shared
/// <see cref="ExConfigRegister{TConfig}"/> reads this on load to decide whether the player has just
/// upgraded the mod and, if so, resets the values named by any matching
/// <see cref="ExConfigMigration"/> back to their coded defaults.
/// </summary>
public interface IExVersionedConfig {
  /// <summary>Mod version that last wrote this config file. Null or absent on first run and on files
  /// written before versioning existed; the store stamps the running mod version on every save.</summary>
  string? ConfigVersion { get; set; }
}

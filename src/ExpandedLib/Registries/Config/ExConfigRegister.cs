using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Config;

/// <summary>
/// Shared loader/saver for a mod's JSON gameplay tunables. Replaces the per-mod copy of the same
/// load-or-default-then-write-back logic: each mod keeps a plain config POCO (with property
/// defaults) plus a thin static accessor that owns one of these stores and exposes
/// <see cref="Config"/> through read-only properties.
/// <para>
/// On <see cref="Load"/> the store reads <c>ModConfig/&lt;fileName&gt;</c> (falling back to defaults
/// if absent or invalid), applies any <see cref="ExConfigMigration"/> triggered by a version change
/// since the file was last written, stamps the running mod version, and writes the file back - so it
/// is created on first run and gains newly added keys on update.
/// </para>
/// </summary>
/// <typeparam name="TConfig">The mod's config POCO; needs a parameterless constructor whose property
/// initialisers define the defaults, and must record the version it was written under.</typeparam>
public sealed class ExConfigRegister<TConfig> : IExConfigAccess
  where TConfig : class, IExVersionedConfig, new() {
  private readonly string _fileName;
  private readonly string _modId;
  private readonly ExConfigMigration[] _migrations;
  private ICoreAPI? _api;
  private PropertyInfo[]? _editableProps;

  /// <summary>The live config. Holds the coded defaults until <see cref="Load"/> runs (and after a
  /// failed load), so accessors are always safe to read.</summary>
  public TConfig Config { get; private set; } = new();

  /// <summary>The owning mod id (also the code typed in <c>/exmod config &lt;mod&gt;</c>).</summary>
  public string ModId => _modId;

  /// <summary>The config file this store reads/writes under <c>ModConfig</c>.</summary>
  public string FileName => _fileName;

  /// <summary>Former names this config file used. On <see cref="Load"/>, if <see cref="FileName"/> is
  /// absent but one of these still exists in <c>ModConfig</c>, it is renamed to the current name (first
  /// match wins) - carrying a player's existing tuning over a config rename instead of regenerating
  /// defaults. Set by the generated accessor from the attribute's <c>LegacyFileNames</c>.</summary>
  public IReadOnlyList<string> LegacyFileNames { get; init; } = [];

  /// <param name="fileName">Config file name written under the game's <c>ModConfig</c> folder
  /// (e.g. <c>"ppex.json"</c>).</param>
  /// <param name="modId">The owning mod id - used to resolve the running version and to tag log lines.</param>
  /// <param name="migrations">Version-driven default resets (see <see cref="ExConfigMigration"/>).</param>
  public ExConfigRegister(
    string fileName,
    string modId,
    params ExConfigMigration[] migrations
  ) {
    _fileName = fileName;
    _modId = modId;
    _migrations = migrations ?? [];
  }

  /// <summary>Loads the config (falling back to defaults), applies any version-change resets, stamps
  /// the current mod version and - on the server - writes the file back. Call once during mod
  /// startup, before any value is read. Both sides read; only the server writes, because in
  /// singleplayer the two sides share one <c>ModConfig</c> folder rather than each holding a local
  /// copy, so two writers would race over the player's file.</summary>
  public void Load(ICoreAPI api) {
    _api = api;
    ExConfigFiles.RenameLegacy(api, _modId, _fileName, LegacyFileNames);

    TConfig config;
    // A file that is present but unreadable is the player's hand-edited data. Preserve a copy
    // before the defaults overwrite it - silently replacing it is how an edited value "reverts".
    bool unreadable = false;
    try {
      config = api.LoadModConfig<TConfig>(_fileName) ?? new TConfig();
      // An empty or whitespace-only file deserializes to null instead of throwing, so it lands here
      // rather than in the catch and would otherwise be the one destructive case that logs nothing.
      unreadable = ExConfigFiles.IsPresentButBlank(_fileName);
    } catch (Exception e) {
      api.Logger.Error(
        "[{0}] Failed to read {1}; using defaults. {2}",
        _modId,
        _fileName,
        e
      );
      config = new TConfig();
      unreadable = true;
    }

    if (unreadable)
      ExConfigFiles.BackupCorrupt(api, _modId, _fileName);

    string current =
      api.ModLoader.GetMod(_modId)?.Info?.Version ?? string.Empty;
    ApplyMigrations(config, current, api.Logger);
    Sanitize(config, api.Logger);
    config.ConfigVersion = current;

    Config = config;

    // Singleplayer runs a client and a server ModLoader on two threads against the SAME ModConfig
    // folder, so a client write lands on top of the server's read - or on the player's edit - and
    // the file comes back as defaults. The recipe catalogue registry already rules the same way:
    // the client keeps its in-memory copy, the server owns the file.
    if (api.Side == EnumAppSide.Server)
      Save();
  }

  /// <summary>
  /// Repairs values a player edited into something that would break the sim: every numeric tunable
  /// outside its valid range (NaN/infinite, or beyond its <see cref="ExConfigRangeAttribute"/> bounds -
  /// which default to non-negative) and any string set to null is reset to its coded default (a missing
  /// key already falls back to the default, and a type-mangled file already fell back to full defaults
  /// in <see cref="Load"/>). The repaired config is written back, so the file is fixed on disk too.
  /// Complex/collection properties (e.g. a recipe catalogue) carry their own repair.
  /// </summary>
  private void Sanitize(TConfig config, ILogger logger) {
    var defaults = new TConfig();
    var reset = new List<string>();

    foreach (
      var p in typeof(TConfig).GetProperties(
        BindingFlags.Public | BindingFlags.Instance
      )
    ) {
      if (
        !p.CanRead
        || !p.CanWrite
        || p.Name == nameof(IExVersionedConfig.ConfigVersion)
      )
        continue;

      object? value = p.GetValue(config);
      bool bad = AsNumber(value) is double n
        ? !InNumericRange(p, n)
        : value is null && p.PropertyType == typeof(string);

      if (bad) {
        p.SetValue(config, p.GetValue(defaults));
        reset.Add(p.Name);
      }
    }

    if (reset.Count > 0)
      logger.Warning(
        "[{0}] Config {1}: reset invalid value(s) to defaults: {2}.",
        _modId,
        _fileName,
        string.Join(", ", reset)
      );
  }

  /// <summary>Resets the fields named by every migration whose <see cref="ExConfigMigration.ToVersion"/>
  /// is crossed by the upgrade from the file's stamped version to the running build.</summary>
  private void ApplyMigrations(TConfig config, string current, ILogger logger) {
    string stored = config.ConfigVersion ?? string.Empty;
    if (stored == current)
      return; // same build - nothing to migrate.

    var defaults = new TConfig();
    foreach (var m in _migrations.OrderBy(m => ParseVersion(m.ToVersion))) {
      bool crossed =
        CompareVersions(m.ToVersion, stored) > 0
        && CompareVersions(m.ToVersion, current) <= 0
        && (
          m.FromVersion == null || CompareVersions(stored, m.FromVersion) >= 0
        );
      if (crossed)
        ResetFields(config, defaults, m, logger);
    }
  }

  private void ResetFields(
    TConfig config,
    TConfig defaults,
    ExConfigMigration m,
    ILogger logger
  ) {
    var writable = typeof(TConfig)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p =>
        p.CanRead
        && p.CanWrite
        && p.Name != nameof(IExVersionedConfig.ConfigVersion)
      );

    IEnumerable<PropertyInfo> toReset;
    if (m.ResetFields is { Length: > 0 }) {
      var wanted = new HashSet<string>(m.ResetFields, StringComparer.Ordinal);
      var byName = writable.ToDictionary(p => p.Name);
      foreach (var name in wanted.Where(n => !byName.ContainsKey(n)))
        logger.Warning(
          "[{0}] Config migration to {1} names unknown field '{2}'; skipped.",
          _modId,
          m.ToVersion,
          name
        );
      toReset = byName.Values.Where(p => wanted.Contains(p.Name));
    } else {
      toReset = writable;
    }

    foreach (var p in toReset)
      p.SetValue(config, p.GetValue(defaults));

    logger.Notification(
      "[{0}] Config: reset {1} to defaults on upgrade to {2}.",
      _modId,
      m.ResetFields is { Length: > 0 }
        ? string.Join(", ", m.ResetFields)
        : "all values",
      m.ToVersion
    );
  }

  /// <summary>Writes the live <see cref="Config"/> back to <c>ModConfig/&lt;fileName&gt;</c>. Called at
  /// the end of <see cref="Load"/>; also public so a runtime config-mutating command (e.g. an admin
  /// toggle) can persist a change made through <see cref="Config"/>.</summary>
  public void Save() {
    try {
      _api?.StoreModConfig(Config, _fileName);
    } catch (Exception e) {
      _api?.Logger.Warning(
        "[{0}] Could not write {1}. {2}",
        _modId,
        _fileName,
        e
      );
    }
  }

  #region IExConfigAccess (runtime /exmod config editing)
  /// <summary>The simple-typed (number/bool/string), read-write tunables this store exposes to the
  /// generic config command - the version stamp and any complex/collection property are excluded.
  /// Cached after first use.</summary>
  private PropertyInfo[] EditableProps =>
    _editableProps ??= typeof(TConfig)
      .GetProperties(BindingFlags.Public | BindingFlags.Instance)
      .Where(p =>
        p.CanRead
        && p.CanWrite
        && p.Name != nameof(IExVersionedConfig.ConfigVersion)
        && IsEditableType(p.PropertyType)
      )
      .ToArray();

  /// <inheritdoc/>
  public IReadOnlyList<string> ValueNames =>
    EditableProps.Select(p => p.Name).ToArray();

  /// <inheritdoc/>
  public bool TryGet(string name, out string canonicalName, out string value) {
    var p = FindProp(name);
    if (p == null) {
      canonicalName = name;
      value = string.Empty;
      return false;
    }

    canonicalName = p.Name;
    value = Format(p.GetValue(Config));
    return true;
  }

  /// <inheritdoc/>
  public ExConfigEditResult Set(string name, string raw) {
    var p = FindProp(name);
    if (p == null)
      return new ExConfigEditResult {
        Status = ExConfigEditStatus.UnknownValue,
        Name = name,
      };

    string oldValue = Format(p.GetValue(Config));

    if (!TryParse(p.PropertyType, raw, out object? parsed, out string expected))
      return new ExConfigEditResult {
        Status = ExConfigEditStatus.ParseFailed,
        Name = p.Name,
        OldValue = oldValue,
        Expected = expected,
      };

    if (AsNumber(parsed) is double n && !InNumericRange(p, n))
      return new ExConfigEditResult {
        Status = ExConfigEditStatus.OutOfRange,
        Name = p.Name,
        OldValue = oldValue,
        Range = FormatRange(p),
      };

    p.SetValue(Config, parsed);
    Save();
    return new ExConfigEditResult {
      Status = ExConfigEditStatus.Ok,
      Name = p.Name,
      OldValue = oldValue,
      NewValue = Format(parsed),
    };
  }

  private PropertyInfo? FindProp(string name) =>
    EditableProps.FirstOrDefault(p =>
      string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)
    );

  private static bool IsEditableType(Type t) =>
    t == typeof(string)
    || t == typeof(bool)
    || t == typeof(int)
    || t == typeof(long)
    || t == typeof(float)
    || t == typeof(double);

  private static string Format(object? v) =>
    v switch {
      float f => f.ToString(CultureInfo.InvariantCulture),
      double d => d.ToString(CultureInfo.InvariantCulture),
      bool b => b ? "true" : "false",
      null => string.Empty,
      _ => Convert.ToString(v, CultureInfo.InvariantCulture) ?? string.Empty,
    };

  /// <summary>Parses <paramref name="raw"/> into <paramref name="type"/> using invariant culture and
  /// lenient boolean words (true/on/yes/1, false/off/no/0). <paramref name="expected"/> is a short
  /// label of the accepted input for the error message.</summary>
  private static bool TryParse(
    Type type,
    string raw,
    out object? value,
    out string expected
  ) {
    value = null;

    if (type == typeof(string)) {
      expected = "text";
      value = raw;
      return true;
    }
    if (type == typeof(bool)) {
      expected = "true/false";
      switch (raw.Trim().ToLowerInvariant()) {
        case "true" or "on" or "yes" or "1":
          value = true;
          return true;
        case "false" or "off" or "no" or "0":
          value = false;
          return true;
        default:
          return false;
      }
    }
    if (type == typeof(int)) {
      expected = "whole number";
      if (
        int.TryParse(
          raw,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out int i
        )
      ) {
        value = i;
        return true;
      }
      return false;
    }
    if (type == typeof(long)) {
      expected = "whole number";
      if (
        long.TryParse(
          raw,
          NumberStyles.Integer,
          CultureInfo.InvariantCulture,
          out long l
        )
      ) {
        value = l;
        return true;
      }
      return false;
    }
    if (type == typeof(float)) {
      expected = "number";
      if (
        float.TryParse(
          raw,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out float f
        )
      ) {
        value = f;
        return true;
      }
      return false;
    }
    if (type == typeof(double)) {
      expected = "number";
      if (
        double.TryParse(
          raw,
          NumberStyles.Float,
          CultureInfo.InvariantCulture,
          out double d
        )
      ) {
        value = d;
        return true;
      }
      return false;
    }

    expected = "value";
    return false;
  }

  /// <summary>The numeric value of <paramref name="v"/> as a <see cref="double"/>, or <c>null</c> for a
  /// non-numeric (string/bool) property.</summary>
  private static double? AsNumber(object? v) =>
    v switch {
      float f => f,
      double d => d,
      int i => i,
      long l => l,
      _ => null,
    };

  /// <summary>The inclusive <c>[min, max]</c> a numeric property accepts: its
  /// <see cref="ExConfigRangeAttribute"/> when present, else the baseline non-negative range
  /// <c>[0, +∞)</c>.</summary>
  private static (double Min, double Max) RangeOf(PropertyInfo p) {
    var attr = p.GetCustomAttribute<ExConfigRangeAttribute>();
    return attr != null ? (attr.Min, attr.Max) : (0d, double.PositiveInfinity);
  }

  /// <summary>Whether <paramref name="n"/> is finite and within the property's accepted range - so an
  /// edit cannot push the config into a value the next load would just reset.</summary>
  private static bool InNumericRange(PropertyInfo p, double n) {
    if (double.IsNaN(n) || double.IsInfinity(n))
      return false;
    var (min, max) = RangeOf(p);
    return n >= min && n <= max;
  }

  /// <summary>A compact, language-neutral description of the property's accepted range for the edit
  /// error: <c>"0..1"</c> for a bounded range, <c>"0+"</c> for a floor only. Avoids <c>&lt;</c>/<c>&gt;</c>
  /// for VTML safety.</summary>
  private static string FormatRange(PropertyInfo p) {
    var (min, max) = RangeOf(p);
    string lo = min.ToString(CultureInfo.InvariantCulture);
    return double.IsPositiveInfinity(max)
      ? $"{lo}+"
      : $"{lo}..{max.ToString(CultureInfo.InvariantCulture)}";
  }
  #endregion

  /// <summary>Parses a mod version (e.g. <c>"0.9.1"</c>, tolerating a <c>-prerelease</c> suffix) into a
  /// comparable <see cref="Version"/>; unparseable or empty versions sort lowest.</summary>
  private static Version ParseVersion(string? v) {
    if (string.IsNullOrWhiteSpace(v))
      return new Version(0, 0);
    int dash = v.IndexOf('-');
    if (dash >= 0)
      v = v[..dash];
    return Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);
  }

  private static int CompareVersions(string? a, string? b) =>
    ParseVersion(a).CompareTo(ParseVersion(b));
}

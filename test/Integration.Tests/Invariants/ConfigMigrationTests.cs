using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using ExpandedLib.Registries.Config;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Guards the config migrations against the version the mod actually ships. A migration only runs
/// when the upgrade crosses its <c>ToVersion</c>, so one that names a version above the modinfo is
/// dead: the build carries the rebalanced code and every existing world keeps the numbers the code
/// no longer expects, while a fresh install gets the new defaults and looks fine. That shipped in
/// smex 0.9.6 - the whole 0.9.7 rebalance was pushed out under a 0.9.6 stamp, so no saved
/// <c>smex_values.json</c> ever took the new molten-flow or furnace values.
/// </summary>
public class ConfigMigrationTests {
  /// <summary>Every config type that declares migrations, paired with its mod folder. Derived from the
  /// test assembly's references, so a new mod is covered without touching this file.</summary>
  private static List<(string Mod, ExConfigMigration[] Migrations)> Corpus() {
    var found = new List<(string, ExConfigMigration[])>();
    foreach (
      AssemblyName name in typeof(ConfigMigrationTests)
        .Assembly.GetReferencedAssemblies()
    ) {
      Assembly asm;
      try {
        asm = Assembly.Load(name);
      } catch (Exception) {
        continue;
      }
      if (!Directory.Exists(Path.Combine(RepoRoot(), "src", name.Name ?? "")))
        continue;

      foreach (Type t in asm.GetTypes()) {
        FieldInfo? f = t.GetField(
          "Migrations",
          BindingFlags.Public | BindingFlags.Static
        );
        if (f?.GetValue(null) is ExConfigMigration[] m && m.Length > 0)
          found.Add((name.Name!, m));
      }
    }
    return found;
  }

  public static TheoryData<string> EveryModWithMigrations() {
    var data = new TheoryData<string>();
    foreach (var (mod, _) in Corpus())
      data.Add(mod);
    return data;
  }

  [Fact]
  public void The_scan_finds_the_configs_that_declare_migrations() {
    // Without this the theory below passes by covering nothing - a renamed field or a mod dropped
    // from the test assembly's references would read exactly like a clean run.
    var corpus = Corpus();
    Assert.True(
      corpus.Count > 0,
      "no config type with a static Migrations array was found in the referenced mods"
    );
  }

  [Theory]
  [MemberData(nameof(EveryModWithMigrations))]
  public void Every_migration_targets_a_version_the_mod_ships(string mod) {
    Version shipped = ModinfoVersion(mod);
    var ahead = Corpus()
      .Where(e => e.Mod == mod)
      .SelectMany(e => e.Migrations)
      .Where(m => ParseVersion(m.ToVersion) > shipped)
      .Select(m => m.ToVersion)
      .ToList();

    Assert.True(
      ahead.Count == 0,
      $"{mod} ships {shipped} but declares migration(s) to {string.Join(", ", ahead)}: "
        + "an upgrade never crosses them, so no existing config takes those defaults"
    );
  }

  private static Version ModinfoVersion(string mod) {
    string path = Path.Combine(RepoRoot(), "src", mod, "modinfo.json");
    Assert.True(File.Exists(path), $"no modinfo.json for {mod} at {path}");

    using JsonDocument doc = JsonDocument.Parse(
      File.ReadAllText(path),
      new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );
    string? raw = doc.RootElement.TryGetProperty("version", out JsonElement v)
      ? v.GetString()
      : null;
    Assert.True(
      !string.IsNullOrWhiteSpace(raw),
      $"{mod}/modinfo.json declares no version"
    );
    return ParseVersion(raw);
  }

  /// <summary>Mirrors the loader's own parse (see <c>ExConfigRegister</c>): a trailing prerelease
  /// suffix is dropped and anything unparseable sorts lowest.</summary>
  private static Version ParseVersion(string? v) {
    if (string.IsNullOrWhiteSpace(v))
      return new Version(0, 0);
    int dash = v.IndexOf('-');
    if (dash >= 0)
      v = v[..dash];
    return Version.TryParse(v, out Version? parsed) ? parsed : new Version(0, 0);
  }

  private static string RepoRoot() {
    DirectoryInfo? dir = new(AppContext.BaseDirectory);
    while (
      dir != null
      && !File.Exists(Path.Combine(dir.FullName, "VintageStory.sln"))
    )
      dir = dir.Parent;
    Assert.True(dir != null, "could not locate repo root (VintageStory.sln)");
    return dir!.FullName;
  }
}

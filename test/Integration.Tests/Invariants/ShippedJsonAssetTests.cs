using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Parses every JSON asset the three mods ship. The game loads these at runtime and reports a
/// syntax error only to the server log, then carries on with the file's whole contents missing -
/// so a corrupt recipe file silently removes every recipe in it while the build and the entire
/// test suite stay green. That is exactly what happened to
/// <c>smex:recipes/grid/blastfurnace.json</c>, which lost the blast furnace door, tap, tuyere and
/// both hoppers to nine stray U+0001 bytes an editor never showed.
/// </summary>
public class ShippedJsonAssetTests {
  public static TheoryData<string> EveryShippedJsonAsset() {
    var data = new TheoryData<string>();
    foreach (string path in AssetFiles())
      data.Add(path);
    return data;
  }

  [Theory]
  [MemberData(nameof(EveryShippedJsonAsset))]
  public void Every_shipped_json_asset_parses(string repoRelativePath) {
    string full = Path.Combine(RepoRoot(), repoRelativePath);
    string text = File.ReadAllText(full);

    var ex = Record.Exception(() =>
      JsonDocument.Parse(
        text,
        new JsonDocumentOptions {
          // The game's loader tolerates both, so the guard must too - otherwise it would fail
          // files that ship and work.
          CommentHandling = JsonCommentHandling.Skip,
          AllowTrailingCommas = true,
        }
      )
    );

    Assert.True(
      ex == null,
      $"{repoRelativePath} is not valid JSON: {ex?.Message}"
    );
  }

  [Theory]
  [MemberData(nameof(EveryShippedJsonAsset))]
  public void No_shipped_json_asset_carries_a_control_character(
    string repoRelativePath
  ) {
    // Tab, LF and CR are the only control characters legal in JSON text. Anything else is
    // invisible in an editor, survives copy/paste, and breaks the parse at a column the error
    // message cannot show - so name the offending offsets explicitly.
    byte[] bytes = File.ReadAllBytes(
      Path.Combine(RepoRoot(), repoRelativePath)
    );
    var bad = new List<string>();
    for (int i = 0; i < bytes.Length; i++) {
      byte b = bytes[i];
      if (b < 0x20 && b != 0x09 && b != 0x0a && b != 0x0d)
        bad.Add($"0x{b:x2} at offset {i}");
    }

    Assert.True(
      bad.Count == 0,
      $"{repoRelativePath} contains {bad.Count} control character(s): "
        + string.Join(", ", bad.Take(8))
    );
  }

  // Every JSON under a mod's assets folder, repo-relative and with forward slashes so the theory
  // labels read the same on any platform. Build outputs (bin/obj) are copies and are skipped.
  private static IEnumerable<string> AssetFiles() {
    string root = RepoRoot();
    string src = Path.Combine(root, "src");
    foreach (
      string file in Directory.EnumerateFiles(
        src,
        "*.json",
        SearchOption.AllDirectories
      )
    ) {
      string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
      if (rel.Contains("/bin/") || rel.Contains("/obj/"))
        continue;
      if (!rel.Contains("/assets/"))
        continue;
      yield return rel;
    }
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

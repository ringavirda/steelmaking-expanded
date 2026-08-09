using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Registries.Config;

/// <summary>
/// Shared helpers for the on-disk config files under the game's <c>ModConfig</c> folder. Used by both
/// the generic <see cref="ExConfigRegister{TConfig}"/> store and the bespoke per-player preferences
/// store, so renaming a config in a new release carries a player's existing file over instead of
/// silently regenerating defaults.
/// </summary>
public static class ExConfigFiles {
  /// <summary>
  /// If <paramref name="fileName"/> does not yet exist under <c>ModConfig</c> but one of
  /// <paramref name="legacyFileNames"/> does, renames that legacy file to the current name (first match
  /// wins). No-op when there are no legacy names, the new file already exists, or no legacy file is
  /// present; any IO failure is logged and swallowed (the caller then falls back to defaults).
  /// </summary>
  public static void RenameLegacy(
    ICoreAPI api,
    string modId,
    string fileName,
    IReadOnlyList<string> legacyFileNames
  ) {
    if (legacyFileNames == null || legacyFileNames.Count == 0)
      return;

    try {
      string dir = GamePaths.ModConfig;
      string target = Path.Combine(dir, fileName);
      if (File.Exists(target))
        return; // new file already present - leave any legacy file untouched.

      foreach (var legacy in legacyFileNames) {
        if (string.IsNullOrWhiteSpace(legacy))
          continue;
        string source = Path.Combine(dir, legacy);
        if (!File.Exists(source))
          continue;

        File.Move(source, target);
        api.Logger.Notification(
          "[{0}] Renamed legacy config '{1}' to '{2}'.",
          modId,
          legacy,
          fileName
        );
        return;
      }
    } catch (Exception e) {
      api.Logger.Warning(
        "[{0}] Could not migrate a legacy config file to '{1}'. {2}",
        modId,
        fileName,
        e
      );
    }
  }

  /// <summary>
  /// True when <paramref name="fileName"/> exists under <c>ModConfig</c> but holds nothing but
  /// whitespace. Such a file deserializes to <c>null</c> rather than throwing, so without this check
  /// it is indistinguishable from "no file yet" and the player's file is replaced by defaults with
  /// nothing at all written to the log.
  /// </summary>
  public static bool IsPresentButBlank(string fileName) {
    try {
      string path = Path.Combine(GamePaths.ModConfig, fileName);
      return File.Exists(path)
        && string.IsNullOrWhiteSpace(File.ReadAllText(path));
    } catch {
      return false; // unreadable for another reason; the caller's own error path covers it.
    }
  }

  /// <summary>
  /// Copies a config file that could not be read to <c>&lt;name&gt;.corrupt</c> before the caller
  /// overwrites it with defaults, so a hand-edited file is never lost without trace. Best effort:
  /// any IO failure is logged and swallowed, since failing startup over a backup would be worse.
  /// </summary>
  public static void BackupCorrupt(ICoreAPI api, string modId, string fileName) {
    try {
      string path = Path.Combine(GamePaths.ModConfig, fileName);
      if (!File.Exists(path))
        return;

      string backup = path + ".corrupt";
      File.Copy(path, backup, overwrite: true);
      api.Logger.Error(
        "[{0}] Config '{1}' could not be read; your file was copied to '{2}' and is being "
          + "replaced with defaults. Fix the copy and rename it back to keep your values.",
        modId,
        fileName,
        Path.GetFileName(backup)
      );
    } catch (Exception e) {
      api.Logger.Warning(
        "[{0}] Could not back up the unreadable config '{1}'. {2}",
        modId,
        fileName,
        e
      );
    }
  }
}

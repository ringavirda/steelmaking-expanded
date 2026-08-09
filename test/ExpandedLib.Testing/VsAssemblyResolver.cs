using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace ExpandedLib.Testing;

/// <summary>
/// Resolves the Vintage Story game assemblies (VintagestoryAPI, VSSurvivalMod, the bundled
/// libraries, …) at runtime from the matching game install. The test/harness projects reference
/// those DLLs with <c>Private=false</c> (they are never shipped), so without this probe a headless
/// <c>dotnet test</c> run cannot load them. Registered automatically via a module initializer in
/// every assembly that includes this type's <see cref="Register"/> call.
///
/// The install is chosen by the game version this assembly was compiled for, so legacy test runs
/// load the right version's DLLs. Rather than a per-TFM <c>#if</c> ladder, the build stamps the
/// version's env-var name (<c>VINTAGE_STORY</c> / <c>VINTAGE_STORY_121</c> / …) and folder slug
/// (<c>1.22</c> / <c>1.21</c> / …) into the assembly as
/// <c>[AssemblyMetadata("GameInstallEnv")]</c> / <c>[AssemblyMetadata("GameSlug")]</c> from the
/// version manifest in <c>src/Directory.Build.props</c>, so adding a game version needs no change
/// here. The path is resolved exactly as the build resolves <c>$(GamePath)</c>: the env-var override
/// if set, otherwise the in-repo install provisioned into <c>.game/&lt;slug&gt;</c> (found by walking
/// up from the test output directory to the repo root).
/// </summary>
public static class VsAssemblyResolver {
  private static readonly object Gate = new();
  private static bool _registered;

  private static readonly string? InstallKey = Metadata("GameInstallEnv");
  private static readonly string? GameSlug = Metadata("GameSlug");

  private static string? Metadata(string key) =>
    typeof(VsAssemblyResolver)
      .Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
      .FirstOrDefault(a => a.Key == key)
      ?.Value;

  /// <summary>Idempotently hooks <see cref="AppDomain.AssemblyResolve"/> to probe the game folders.</summary>
  public static void Register() {
    lock (Gate) {
      if (_registered)
        return;
      _registered = true;
    }

    string? vs = ResolveInstallPath();
    if (string.IsNullOrEmpty(vs))
      return;

    string[] dirs = [vs, Path.Combine(vs, "Lib"), Path.Combine(vs, "Mods")];

    AppDomain.CurrentDomain.AssemblyResolve += (_, args) => {
      string? name = new AssemblyName(args.Name).Name;
      if (name == null)
        return null;
      foreach (string dir in dirs) {
        string path = Path.Combine(dir, name + ".dll");
        if (File.Exists(path))
          return Assembly.LoadFrom(path);
      }
      return null;
    };
  }

  /// <summary>The install path for this TFM: the <see cref="InstallKey"/> environment variable if set
  /// (the override CI uses), otherwise the in-repo install at <c>.game/&lt;slug&gt;</c>, found by
  /// walking up from the test output directory to the repo root. Null when neither yields a path.</summary>
  private static string? ResolveInstallPath() {
    if (!string.IsNullOrEmpty(InstallKey)) {
      string? fromEnv = Environment.GetEnvironmentVariable(InstallKey);
      if (!string.IsNullOrEmpty(fromEnv))
        return fromEnv;
    }
    return FindRepoGameInstall();
  }

  /// <summary>Walks up from the test output directory looking for a provisioned
  /// <c>.game/&lt;slug&gt;</c> that carries the game assemblies. Null if the slug is unknown or no
  /// such folder exists up the tree.</summary>
  private static string? FindRepoGameInstall() {
    if (string.IsNullOrEmpty(GameSlug))
      return null;
    for (
      DirectoryInfo? dir = new(AppContext.BaseDirectory);
      dir != null;
      dir = dir.Parent
    ) {
      string candidate = Path.Combine(dir.FullName, ".game", GameSlug);
      if (File.Exists(Path.Combine(candidate, "VintagestoryAPI.dll")))
        return candidate;
    }
    return null;
  }
}

internal static class HarnessModuleInitializer {
  // Deliberately used in a class library: the resolver must be live before any harness type that
  // references the game assemblies is touched. Safe here - it only hooks AssemblyResolve.
#pragma warning disable CA2255
  [ModuleInitializer]
#pragma warning restore CA2255
  internal static void Init() => VsAssemblyResolver.Register();
}

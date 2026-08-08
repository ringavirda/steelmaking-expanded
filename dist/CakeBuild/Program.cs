using System;
using System.Collections.Generic;
using System.IO;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Build;
using Cake.Common.Tools.DotNet.MSBuild;
using Cake.Common.Tools.DotNet.Publish;
using Cake.Core;
using Cake.Frosting;
using Cake.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;

namespace CakeBuild;

public static class Program
{
  public static int Main(string[] args)
  {
    return new CakeHost().UseContext<BuildContext>().Run(args);
  }
}

/// <summary>One buildable mod project in the monorepo.</summary>
public record ModProject(string Folder, string ModId, string Version);

/// <summary>A supported game version to publish for: its TFM, the game version stamped into the
/// packaged modinfo, and whether it's the current (non-legacy) target. Keep in sync with the version
/// manifest in src/Directory.Build.props (Cake can't read MSBuild, so this is the one duplication -
/// same as the CI workflows).</summary>
public record GameTarget(string Tfm, string GameVersion, bool IsCurrent);

public class BuildContext : FrostingContext
{
  // Build order matters: exlib first (the shared lib both mods reference), then ppex
  // (referenced by smex), then smex.
  public static readonly string[] ProjectFolders =
  [
    "ExpandedLib",
    "PipesAndPowerExpanded",
    "SteelmakingExpanded",
  ];

  // Every supported game version. The legacy ones (IsCurrent=false) build with -p:Legacy=true and
  // land in a per-TFM output path; their packaged modinfo gets its game dependency rewritten.
  public static readonly GameTarget[] GameTargets =
  [
    new GameTarget("net10.0", "1.22.0", true),
    new GameTarget("net8.0", "1.21.0", false),
    new GameTarget("net7.0", "1.20.0", false),
  ];

  // The game dependency the source modinfo.json files declare (= the current version's floor).
  public const string SourceGameVersion = "1.22.0";

  public string BuildConfiguration { get; }
  public bool SkipJsonValidation { get; }
  public List<ModProject> Projects { get; } = [];

  public BuildContext(ICakeContext context)
    : base(context)
  {
    BuildConfiguration = context.Argument("configuration", "Release");
    SkipJsonValidation = context.Argument("skipJsonValidation", false);

    foreach (var folder in ProjectFolders)
    {
      var modInfo = context.DeserializeJsonFromFile<ModInfo>(
        $"../../src/{folder}/modinfo.json"
      );
      Projects.Add(new ModProject(folder, modInfo.ModID, modInfo.Version));
    }
  }

  /// <summary>The publish output for a project+target. The current version uses the flat
  /// Mods/mod path; legacy targets append their TFM (see the mod csproj OutputPath).</summary>
  public string PublishDir(ModProject project, GameTarget target) =>
    target.IsCurrent
      ? $"../../src/{project.Folder}/bin/{BuildConfiguration}/Mods/mod/publish"
      : $"../../src/{project.Folder}/bin/{BuildConfiguration}/{target.Tfm}/Mods/mod/publish";
}

[TaskName("ValidateJson")]
public sealed class ValidateJsonTask : FrostingTask<BuildContext>
{
  public override void Run(BuildContext context)
  {
    if (context.SkipJsonValidation)
      return;

    foreach (var project in context.Projects)
    {
      var jsonFiles = context.GetFiles(
        $"../../src/{project.Folder}/assets/**/*.json"
      );
      foreach (var file in jsonFiles)
      {
        try
        {
          JToken.Parse(File.ReadAllText(file.FullPath));
        }
        catch (JsonException ex)
        {
          throw new Exception(
            $"Validation failed for JSON file: {file.FullPath}{Environment.NewLine}{ex.Message}",
            ex
          );
        }
      }
    }
  }
}

[TaskName("Build")]
[IsDependentOn(typeof(ValidateJsonTask))]
public sealed class BuildTask : FrostingTask<BuildContext>
{
  public override void Run(BuildContext context)
  {
    foreach (var project in context.Projects)
    {
      string csproj = $"../../src/{project.Folder}/{project.Folder}.csproj";
      // Wipe the whole bin so stale per-version outputs can't leak into a package.
      string binDir =
        $"../../src/{project.Folder}/bin/{context.BuildConfiguration}";
      context.EnsureDirectoryExists(binDir);
      context.CleanDirectory(binDir);

      foreach (var target in BuildContext.GameTargets)
      {
        context.DotNetPublish(
          csproj,
          new DotNetPublishSettings
          {
            Configuration = context.BuildConfiguration,
            Framework = target.Tfm,
            // -p:Legacy=true makes the mod multi-target so the legacy TFMs exist; harmless for the
            // current one (it stays the flat, non-legacy output).
            MSBuildSettings = new DotNetMSBuildSettings().WithProperty(
              "Legacy",
              "true"
            ),
          }
        );
      }
    }
  }
}

[TaskName("Package")]
[IsDependentOn(typeof(BuildTask))]
public sealed class PackageTask : FrostingTask<BuildContext>
{
  public override void Run(BuildContext context)
  {
    context.EnsureDirectoryExists("../Releases");
    context.CleanDirectory("../Releases");

    // One archive per (game version, mod), grouped into a per-version folder. Legacy
    // targets get a trailing game-version suffix so the files are distinguishable; the
    // current version stays unsuffixed:
    //   Releases/<gameVersion>/<modid>_<modVersion>.zip            (current)
    //   Releases/<gameVersion>/<modid>_<modVersion>_<gameVersion>.zip (legacy)
    foreach (var target in BuildContext.GameTargets)
    {
      foreach (var project in context.Projects)
      {
        string stageDir = $"../Releases/{target.GameVersion}/{project.ModId}";
        context.EnsureDirectoryExists(stageDir);

        context.CopyFiles($"{context.PublishDir(project, target)}/*", stageDir);
        // Copy assets from the per-target PUBLISH output, NOT from raw source: the csproj applies
        // per-game-version `Content Remove` filtering (e.g. legacy-only patches whose crushed codes
        // don't resolve on newer versions), and only the publish output reflects it. Copying source
        // assets here bypassed that and shipped both versions' files into every package, which
        // crashed clients on world-load when the wrong patch referenced a non-existent stack.
        if (context.DirectoryExists($"{context.PublishDir(project, target)}/assets"))
          context.CopyDirectory(
            $"{context.PublishDir(project, target)}/assets",
            $"{stageDir}/assets"
          );
        if (context.FileExists($"../../src/{project.Folder}/modicon.png"))
          context.CopyFile(
            $"../../src/{project.Folder}/modicon.png",
            $"{stageDir}/modicon.png"
          );

        // Authoritative modinfo: the source declares the current game version, so point the game
        // dependency at this target's version (no-op for the current one).
        string modinfoSource = File.ReadAllText(
          $"../../src/{project.Folder}/modinfo.json"
        );
        string modinfo = modinfoSource.Replace(
          $"\"game\": \"{BuildContext.SourceGameVersion}\"",
          $"\"game\": \"{target.GameVersion}\""
        );
        // The rewrite is a literal match, so a source modinfo that spells its game dependency any
        // other way ("1.21" for "1.21.0", say) silently no-ops and ships a legacy package carrying
        // the CURRENT game dependency, which the target game then rejects. Fail the build instead.
        if (!target.IsCurrent && modinfo == modinfoSource)
          throw new Exception(
            $"{project.Folder}/modinfo.json: expected to rewrite \"game\": \"{BuildContext.SourceGameVersion}\" "
              + $"to \"{target.GameVersion}\", but the text was unchanged. The legacy package would ship "
              + "the current game dependency and be rejected by the game."
          );
        File.WriteAllText($"{stageDir}/modinfo.json", modinfo);

        string versionSuffix = target.IsCurrent ? "" : $"_{target.GameVersion}";
        context.Zip(
          stageDir,
          $"../Releases/{target.GameVersion}/{project.ModId}_{project.Version}{versionSuffix}.zip"
        );
      }
    }
  }
}

[TaskName("PackageTesting")]
[IsDependentOn(typeof(PackageTask))]
public sealed class PackageTestingTask : FrostingTask<BuildContext>
{
  // The headless test harness (test/ExpandedLib.Testing) is a DEVELOPER library, not a game mod, so
  // it isn't a mod zip and isn't on NuGet (its API still moves a lot release to release). We ship it
  // as a dev bundle attached to the GitHub release: ExpandedLib.Testing.dll plus the exlib.dll it
  // compiles against (exlib's AssemblyName is "exlib"), which a downstream test project references
  // directly (the game assemblies and NSubstitute the consumer supplies - see the wiki
  // "Consuming outside this repo").
  //
  // Built for the CURRENT game version only (net10.0 / 1.22); on 1.20/1.21 reference it from source.
  const string BundleReadme =
    "ExpandedLib.Testing - headless Vintage Story test harness (dev library)\n"
    + "\n"
    + "Built for the current game version (1.22 / net10.0). Add both DLLs to your test project\n"
    + "with <Private>false</Private>, reference VintagestoryAPI/VSSurvivalMod/VSEssentials from\n"
    + "your own game install, add NSubstitute + xUnit from NuGet, and call\n"
    + "VsAssemblyResolver.Register() + TestLang.Init() from a [ModuleInitializer]. See the wiki:\n"
    + "https://github.com/ringavirda/modding-vsexpanded/wiki/Testing-Harness\n";

  public override void Run(BuildContext context)
  {
    var current = Array.Find(BuildContext.GameTargets, t => t.IsCurrent)!;
    var exlib = context.Projects.Find(p => p.Folder == "ExpandedLib")!;

    // Build the harness for the current target (single-TFM => flat bin/<config> output).
    context.DotNetBuild(
      "../../test/ExpandedLib.Testing/ExpandedLib.Testing.csproj",
      new DotNetBuildSettings
      {
        Configuration = context.BuildConfiguration,
        Framework = current.Tfm,
      }
    );

    // Hyphen, not a dot, in the basename: GitHub's release-asset uploader sniffs content type from
    // the filename and rejects a dotted name segment (exlib.testing_x.zip) with "we can't process
    // this file". exlib-testing_<version>.zip keeps the _<version> convention and uploads cleanly.
    string stageDir = $"../Releases/{current.GameVersion}/exlib-testing";
    context.EnsureDirectoryExists(stageDir);

    context.CopyFile(
      $"../../test/ExpandedLib.Testing/bin/{context.BuildConfiguration}/ExpandedLib.Testing.dll",
      $"{stageDir}/ExpandedLib.Testing.dll"
    );
    // exlib.dll comes from exlib's own publish output (the harness references it Private=false, so
    // it isn't copied into the harness bin). The assembly file is exlib.dll (AssemblyName "exlib").
    context.CopyFile(
      $"{context.PublishDir(exlib, current)}/exlib.dll",
      $"{stageDir}/exlib.dll"
    );
    File.WriteAllText($"{stageDir}/README.txt", BundleReadme);

    context.Zip(
      stageDir,
      $"../Releases/{current.GameVersion}/exlib-testing_{exlib.Version}.zip"
    );
  }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTestingTask))]
public class DefaultTask : FrostingTask { }

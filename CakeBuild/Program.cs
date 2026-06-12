using System;
using System.Collections.Generic;
using System.IO;
using Cake.Common;
using Cake.Common.IO;
using Cake.Common.Tools.DotNet;
using Cake.Common.Tools.DotNet.Clean;
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
        $"../{folder}/modinfo.json"
      );
      Projects.Add(new ModProject(folder, modInfo.ModID, modInfo.Version));
    }
  }
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
      var jsonFiles = context.GetFiles($"../{project.Folder}/assets/**/*.json");
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
      string csproj = $"../{project.Folder}/{project.Folder}.csproj";
      context.DotNetClean(
        csproj,
        new DotNetCleanSettings { Configuration = context.BuildConfiguration }
      );
      context.DotNetPublish(
        csproj,
        new DotNetPublishSettings { Configuration = context.BuildConfiguration }
      );
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

    foreach (var project in context.Projects)
    {
      string releaseDir = $"../Releases/{project.ModId}";
      context.EnsureDirectoryExists(releaseDir);

      context.CopyFiles(
        $"../{project.Folder}/bin/{context.BuildConfiguration}/Mods/mod/publish/*",
        releaseDir
      );
      if (context.DirectoryExists($"../{project.Folder}/assets"))
        context.CopyDirectory(
          $"../{project.Folder}/assets",
          $"{releaseDir}/assets"
        );
      context.CopyFile(
        $"../{project.Folder}/modinfo.json",
        $"{releaseDir}/modinfo.json"
      );
      if (context.FileExists($"../{project.Folder}/modicon.png"))
        context.CopyFile(
          $"../{project.Folder}/modicon.png",
          $"{releaseDir}/modicon.png"
        );

      context.Zip(
        releaseDir,
        $"../Releases/{project.ModId}_{project.Version}.zip"
      );
    }
  }
}

[TaskName("Default")]
[IsDependentOn(typeof(PackageTask))]
public class DefaultTask : FrostingTask { }

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NSubstitute;
using SteelmakingExpanded.BlockNetworkMolten;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// The molten surface a canal piece draws against the footprint its blocktype declares. A cell holds
/// one pool, so every arm of a bend, tee or cross is one surface at one height and all of them have
/// to be drawn. Selecting one box by fill level instead - which is what the renderer did - left a tee
/// showing its through-channel under half full and only its side stub over it, and a cross showing
/// one arm out of three.
/// </summary>
public class MoltenSurfaceTests {
  /// <summary>A renderer that exposes how many meshes it uploaded for its footprint.</summary>
  private sealed class ProbeRenderer : MoltenRenderer {
    public ProbeRenderer(ICoreClientAPI api, Cuboidf[] boxes)
      : base(new BlockPos(0, 0, 0), api, boxes) { }

    public int MeshCount => MeshRefs.Length;
  }

  public static TheoryData<string> EveryMultiArmCanal() {
    var data = new TheoryData<string>();
    foreach (var (path, _) in MultiArmFootprints())
      data.Add(path);
    return data;
  }

  [Fact]
  public void The_scan_finds_the_canal_pieces_with_more_than_one_arm() {
    // Without this the theory below passes by covering nothing: move the quads or rename the
    // attribute and an empty corpus reads exactly like a clean run.
    var found = MultiArmFootprints();
    Assert.True(
      found.Count > 0,
      "no shipped molten blocktype declares a multi-box fillQuadsByLevel"
    );
    foreach (string piece in new[] { "tjunction", "xjunction", "bend", "start" })
      Assert.True(
        found.Any(f => f.Path.Contains(piece, StringComparison.Ordinal)),
        $"the {piece} no longer declares a multi-box footprint - the case this guards is gone"
      );
  }

  [Theory]
  [MemberData(nameof(EveryMultiArmCanal))]
  public void Every_arm_of_a_canal_piece_is_drawn_as_one_surface(
    string repoRelativePath
  ) {
    Cuboidf[] boxes = MultiArmFootprints()
      .First(f => f.Path == repoRelativePath)
      .Boxes;
    Assert.True(boxes.Length > 1, "premise: this piece has several arms");

    var probe = new ProbeRenderer(Substitute.For<ICoreClientAPI>(), boxes);

    // One mesh for the whole footprint: every arm is drawn, every frame, at the same surface height.
    // More than one means the renderer is picking between them and the rest of the channel is dry.
    Assert.Equal(1, probe.MeshCount);
  }

  /// <summary>Every shipped molten blocktype whose fill footprint is more than one box, with the
  /// boxes parsed the way the block entity parses them.</summary>
  private static List<(string Path, Cuboidf[] Boxes)> MultiArmFootprints() {
    var found = new List<(string, Cuboidf[])>();
    string root = RepoRoot();
    string molten = Path.Combine(
      root,
      "src",
      "SteelmakingExpanded",
      "assets",
      "smex",
      "blocktypes",
      "molten"
    );

    foreach (
      string file in Directory.EnumerateFiles(
        molten,
        "*.json",
        SearchOption.AllDirectories
      )
    ) {
      JToken? quads = JObject.Parse(File.ReadAllText(file))["attributes"]?[
        "fillQuadsByLevel"
      ];
      if (quads == null)
        continue;

      Cuboidf[] boxes = FillQuads.BoxesFrom(
        new JsonObject(quads),
        new Cuboidf()
      );
      if (boxes.Length > 1)
        found.Add((Path.GetRelativePath(root, file).Replace('\\', '/'), boxes));
    }
    return found;
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

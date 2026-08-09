using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace Integration.Tests;

/// <summary>
/// Guards the stretch between a looping clip's last keyframe and its first. That stretch is not a
/// seam the animator skips over - it is an ordinary interpolation segment, rendered like any other,
/// because the live frame space is <c>[0, quantityframes)</c> while the last keyframe sits at
/// <c>quantityframes - 1</c>. Author a shaft as a full turn ending back at its starting value and
/// that one-frame segment has to unwind the whole revolution backwards, which reads in game as the
/// machine snapping to its unbuilt pose once per cycle. Vanilla's own machines all end a turn one
/// frame short - <c>360 * (frames - 1) / frames</c> - and set <c>rotShortestDistance</c>.
/// </summary>
public class LoopingAnimationTests {
  /// <summary>
  /// Past this the wrap is a real unwind rather than the last slice of the turn. Half a revolution
  /// is far beyond any legitimate one-frame step and well clear of the small residuals a clip
  /// carries when its elements are still moving through the loop point.
  /// </summary>
  private const double UnwindDegrees = 180.0;

  private static readonly string[] Axes = ["X", "Y", "Z"];

  public static TheoryData<string> EveryShippedShape() {
    var data = new TheoryData<string>();
    foreach (string path in ShapeFiles())
      data.Add(path);
    return data;
  }

  [Theory]
  [MemberData(nameof(EveryShippedShape))]
  public void No_looping_clip_unwinds_a_whole_turn_across_its_wrap(
    string repoRelativePath
  ) {
    var offenders = new List<string>();

    foreach (Clip clip in LoopingClips(repoRelativePath)) {
      foreach (
        (
          string element,
          JsonElement first,
          JsonElement last
        ) in clip.FirstAndLastPerElement()
      ) {
        foreach (string axis in Axes) {
          double from = Rotation(first, axis);
          double to = Rotation(last, axis);
          if (Math.Abs(to - from) < UnwindDegrees)
            continue;
          if (Flagged(last, axis))
            continue;
          offenders.Add(
            string.Format(
              CultureInfo.InvariantCulture,
              "clip '{0}' element '{1}' rotation{2} wraps {3:0.##} -> {4:0.##}"
                + " ({5:0.##} deg) with no rotShortestDistance{2}",
              clip.Code,
              element,
              axis,
              to,
              from,
              Math.Abs(to - from)
            )
          );
        }
      }
    }

    Assert.True(
      offenders.Count == 0,
      $"{repoRelativePath}: {string.Join("; ", offenders)}"
    );
  }

  [Theory]
  [MemberData(nameof(EveryShippedShape))]
  public void No_looping_clip_leaves_an_element_posed_only_at_one_end(
    string repoRelativePath
  ) {
    // An element keyframed partway through the clip but not at its first keyframe holds that pose
    // from the loop point until the animator next reaches it, then moves off - a hitch at the same
    // place every cycle. Its counterpart, keyframed at the start but not at the end, drifts back to
    // frame 0 across the wrap rather than through the motion the artist drew.
    var offenders = new List<string>();

    foreach (Clip clip in LoopingClips(repoRelativePath)) {
      int first = clip.Frames.First();
      int last = clip.Frames.Last();
      foreach (string element in clip.Elements()) {
        bool atFirst = clip.Poses(element).Any(p => p.frame == first);
        bool atLast = clip.Poses(element).Any(p => p.frame == last);
        if (atFirst && atLast)
          continue;
        offenders.Add(
          $"clip '{clip.Code}' element '{element}' keyframed at "
            + (atFirst ? $"{first} but not {last}" : $"{last} but not {first}")
        );
      }
    }

    Assert.True(
      offenders.Count == 0,
      $"{repoRelativePath}: {string.Join("; ", offenders)}"
    );
  }

  #region Shape reading

  /// <summary>
  /// Only clips that say <c>Repeat</c> outright. A clip that stops or eases out at its end never
  /// renders the wrap segment, so the rule does not apply to it.
  /// </summary>
  private static IEnumerable<Clip> LoopingClips(string repoRelativePath) {
    using var doc = JsonDocument.Parse(
      File.ReadAllText(Path.Combine(RepoRoot(), repoRelativePath)),
      new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );

    if (
      !doc.RootElement.TryGetProperty("animations", out JsonElement anims)
      || anims.ValueKind != JsonValueKind.Array
    )
      yield break;

    foreach (JsonElement anim in anims.EnumerateArray()) {
      if (
        !anim.TryGetProperty("onAnimationEnd", out JsonElement end)
        || end.GetString() != "Repeat"
      )
        continue;
      if (
        !anim.TryGetProperty("keyframes", out JsonElement keys)
        || keys.ValueKind != JsonValueKind.Array
      )
        continue;

      var frames = keys.EnumerateArray().ToList();
      // A single keyframe is a held pose - there is nothing to interpolate and no wrap to get wrong.
      if (frames.Count < 2)
        continue;

      yield return new Clip(
        anim.TryGetProperty("code", out JsonElement code)
          ? code.GetString() ?? "?"
          : "?",
        frames
      );
    }
  }

  private sealed record Clip(string Code, List<JsonElement> Keyframes) {
    public IEnumerable<int> Frames =>
      Keyframes.Select(k => k.GetProperty("frame").GetInt32());

    public IEnumerable<string> Elements() =>
      Keyframes
        .SelectMany(k =>
          k.TryGetProperty("elements", out JsonElement els)
          && els.ValueKind == JsonValueKind.Object
            ? els.EnumerateObject().Select(p => p.Name)
            : []
        )
        .Distinct();

    public IEnumerable<(int frame, JsonElement pose)> Poses(string element) {
      foreach (JsonElement key in Keyframes) {
        if (
          key.TryGetProperty("elements", out JsonElement els)
          && els.ValueKind == JsonValueKind.Object
          && els.TryGetProperty(element, out JsonElement pose)
        )
          yield return (key.GetProperty("frame").GetInt32(), pose);
      }
    }

    /// <summary>
    /// Each element's own first and last poses. The animator resolves keyframes per element, so an
    /// element's wrap runs between the frames IT is posed at, not the clip's outermost frames.
    /// </summary>
    public IEnumerable<(
      string element,
      JsonElement first,
      JsonElement last
    )> FirstAndLastPerElement() {
      foreach (string element in Elements()) {
        var poses = Poses(element).ToList();
        if (poses.Count > 0)
          yield return (element, poses[0].pose, poses[^1].pose);
      }
    }
  }

  private static double Rotation(JsonElement pose, string axis) =>
    pose.TryGetProperty("rotation" + axis, out JsonElement v)
    && v.ValueKind == JsonValueKind.Number
      ? v.GetDouble()
      : 0.0;

  private static bool Flagged(JsonElement pose, string axis) =>
    pose.TryGetProperty("rotShortestDistance" + axis, out JsonElement flag)
    && flag.ValueKind == JsonValueKind.True;

  private static IEnumerable<string> ShapeFiles() {
    string root = RepoRoot();
    foreach (
      string file in Directory.EnumerateFiles(
        Path.Combine(root, "src"),
        "*.json",
        SearchOption.AllDirectories
      )
    ) {
      string rel = Path.GetRelativePath(root, file).Replace('\\', '/');
      if (rel.Contains("/bin/") || rel.Contains("/obj/"))
        continue;
      if (!rel.Contains("/assets/") || !rel.Contains("/shapes/"))
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

  #endregion
}

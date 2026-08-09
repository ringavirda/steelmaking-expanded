using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Regression guard for the RCC mega-block break JSON, which the headless harness can't load (blocks
/// are configured by hand, not from assets). Reads the shipped block JSON directly and pins the two
/// standing rules: every machine placed from a crafted item hands that item back when broken, and
/// breaking one recovers all of its construction materials. Only the Bessemer converter withholds a
/// self-drop, because it is spawned by its control block and no vessel item exists to return.
/// </summary>
public class MegablockDropTierTests {
  private const string Bessemer =
    "src/SteelmakingExpanded/assets/smex/blocktypes/converter/bessemer.json";
  private const string Watt =
    "src/PipesAndPowerExpanded/assets/ppex/blocktypes/engine/watt.json";
  private const string EngineCornish =
    "src/PipesAndPowerExpanded/assets/ppex/blocktypes/engine/cornish.json";
  private const string Lancashire =
    "src/PipesAndPowerExpanded/assets/ppex/blocktypes/boiler/lancashire.json";
  private const string BoilerCornish =
    "src/PipesAndPowerExpanded/assets/ppex/blocktypes/boiler/cornish.json";

  private static readonly string[] AllMegablocks =
  [
    Bessemer,
    Watt,
    EngineCornish,
    Lancashire,
    BoilerCornish,
  ];

  #region Converter (control-spawned: no self-drop)

  [Fact]
  public void Bessemer_converter_does_not_drop_itself_as_a_block() {
    // An explicit empty "drops" array suppresses the auto-populated self-drop. Without it the
    // registry hands the block its own code as a drop - the reported bug.
    JsonElement block = Block(Bessemer);
    Assert.True(
      block.TryGetProperty("drops", out JsonElement drops),
      "bessemer.json must declare \"drops\": [] to suppress the self-drop"
    );
    Assert.Equal(JsonValueKind.Array, drops.ValueKind);
    Assert.Equal(0, drops.GetArrayLength());
  }

  #endregion

  #region Crafted frames (self-drop is correct)

  [Theory]
  [InlineData(Watt)]
  [InlineData(EngineCornish)]
  [InlineData(Lancashire)]
  [InlineData(BoilerCornish)]
  public void A_machine_placed_from_a_crafted_item_hands_it_back(string path) {
    // Engines and boilers are placed from a crafted frame item, so breaking one must return that
    // item on top of the construction materials. Withholding it made taking a machine down to move
    // it a net loss - the converter is the only machine with no item to return.
    JsonElement block = Block(path);
    bool suppressesSelfDrop =
      block.TryGetProperty("drops", out JsonElement drops)
      && drops.ValueKind == JsonValueKind.Array
      && drops.GetArrayLength() == 0;
    Assert.False(
      suppressesSelfDrop,
      $"{path} should keep its frame self-drop (no empty \"drops\")"
    );
  }

  #endregion

  #region Construction cost (salvage base)

  // The break salvage scatters brokenDropsRatio (80%) of the consumed stacks across EVERY completed
  // stage. Vanilla rcc.GetDrops omits the LAST stage (a `i < CurrentCompletedStage` off-by-one), which
  // robbed the salvage of the most expensive stage - the Lancashire casing - so a fully built boiler
  // refunded ~40% instead of 80%. ExRightClickConstructable now includes the final stage. This pins
  // the full per-material construction cost the 80% is taken from, so a stage edit can't silently shift
  // it again. (The 1.22 drop code is vanilla-backed and the legacy reimpl is excluded from this target,
  // so the totals are asserted off the shipped JSON.)
  [Fact]
  public void Lancashire_boiler_full_construction_cost_is_pinned() {
    Dictionary<string, int> cost = ConstructionCost(Block(Lancashire));

    Assert.Equal(34, cost["metalplate-steel"]); // 10 + 8 + 16
    Assert.Equal(24, cost["metalnailsandstrips-*"]); // 8 + 8 + 8
    Assert.Equal(10, cost["rod-steel"]); // 4 + 6
    Assert.Equal(60, cost["game:burnedbrick-fire"]); // 12 + 48
  }

  // Sums every stage's requireStacks quantity by ingredient code (the full build cost).
  private static Dictionary<string, int> ConstructionCost(JsonElement block) {
    var totals = new Dictionary<string, int>();
    foreach (
      JsonElement stage in Constructable(block)
        .GetProperty("stages")
        .EnumerateArray()
    ) {
      if (!stage.TryGetProperty("requireStacks", out JsonElement stacks))
        continue;
      foreach (JsonElement ing in stacks.EnumerateArray()) {
        string code = ing.GetProperty("code").GetString()!;
        totals[code] =
          totals.GetValueOrDefault(code)
          + ing.GetProperty("quantity").GetInt32();
      }
    }
    return totals;
  }

  #endregion

  #region Salvage and mining (shared rules)

  // Breaking a machine intact returns everything it cost. The salvage tax only discouraged
  // rebuilding a plant, which is a normal part of laying one out; a burst boiler is still penalised,
  // through BoilerExplosionDropRatio.
  [Fact]
  public void Breaking_a_machine_intact_recovers_all_of_its_materials() {
    Assert.Equal(1.0f, new SmexConfig().RccBrokenDropsRatio, 3);
    Assert.Equal(
      1.0f,
      new PipesAndPowerExpanded.PpexConfig().RccBrokenDropsRatio,
      3
    );
  }

  // No machine gates breaking on a pickaxe tier. The tiers only ever delayed a player who would
  // have the metal to build the machine in the first place.
  [Theory]
  [MemberData(nameof(EveryMegablock))]
  public void No_machine_demands_a_mining_tier(string path) {
    Assert.False(
      Block(path).TryGetProperty("requiredMiningTier", out _),
      $"{path} must not gate breaking on a pickaxe tier"
    );
  }

  [Theory]
  [MemberData(nameof(EveryMegablock))]
  public void No_machine_carries_a_json_drop_ratio(string path) {
    // The salvage fraction lives on the player-tunable config (RccBrokenDropsRatio), read live via
    // ExRccSettings, so it must not be pinned per block in JSON.
    Assert.False(
      Constructable(Block(path)).TryGetProperty("brokenDropsRatio", out _),
      $"{path} brokenDropsRatio must live in config, not the block JSON"
    );
  }

  public static TheoryData<string> EveryMegablock() {
    var data = new TheoryData<string>();
    foreach (string path in AllMegablocks)
      data.Add(path);
    return data;
  }

  #endregion

  #region Asset loading

  // The ExRightClickConstructable entity behavior's properties node (holds brokenDropsRatio + stages).
  private static JsonElement Constructable(JsonElement block) {
    foreach (
      JsonElement b in block.GetProperty("entityBehaviors").EnumerateArray()
    ) {
      if (
        b.TryGetProperty("name", out JsonElement name)
        && name.GetString() == "ExRightClickConstructable"
      )
        return b.GetProperty("properties");
    }
    throw new Xunit.Sdk.XunitException(
      "block has no ExRightClickConstructable behavior"
    );
  }

  private static JsonElement Block(string repoRelativePath) {
    string full = Path.Combine(
      RepoRoot(),
      repoRelativePath.Replace('/', Path.DirectorySeparatorChar)
    );
    Assert.True(File.Exists(full), $"missing asset: {full}");
    using var doc = JsonDocument.Parse(
      File.ReadAllText(full),
      new JsonDocumentOptions {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
      }
    );
    return doc.RootElement.Clone();
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

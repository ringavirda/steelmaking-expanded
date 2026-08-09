using System.Collections.Generic;
using System.Linq;
using ExpandedLib.Registries.Recipes;
using ExpandedLib.Testing;
using Newtonsoft.Json.Linq;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>
/// The generic recipe-cost adjuster: a catalogue entry carries a self-contained
/// <see cref="RecipeProfileCost"/> per profile. Tests cover extracting "normal" from a live recipe,
/// scale-filling an alternate profile, applying a profile to grid recipes (ingredients + pinned output)
/// and to RCC blocks (per-stage), and reconciling a hand-edited file.
/// </summary>
public class ExRecipeCostsTests {
  // 1.22 resolves grid ingredients into a CraftingRecipeIngredient[] property; 1.20/1.21 use a
  // GridRecipeIngredient[] field (a CraftingRecipeIngredient subclass). The tests mutate the same
  // ingredient instance they pass in and assert on it afterwards, so on legacy the instances
  // themselves must be GridRecipeIngredients to live in that field - hence the per-version Ing.
#if GAME_GE_1_22
  private static CraftingRecipeIngredient Ing(string code, int qty) =>
    new() { Code = new AssetLocation(code), Quantity = qty };
#else
  private static CraftingRecipeIngredient Ing(string code, int qty) =>
    new GridRecipeIngredient { Code = new AssetLocation(code), Quantity = qty };
#endif

  private static GridRecipe GridRecipe(
    string output,
    params CraftingRecipeIngredient[] ings
  ) {
    var recipe = new GridRecipe {
      Output = new CraftingRecipeIngredient {
        Code = new AssetLocation(output),
      },
    };
    SetResolvedIngredients(recipe, ings);
    return recipe;
  }

  /// <summary>Stores the resolved ingredients into whichever member this game version exposes,
  /// keeping the same instances so a later Apply mutates the objects the test holds.</summary>
  private static void SetResolvedIngredients(
    GridRecipe recipe,
    CraftingRecipeIngredient[] ings
  ) =>
#if GAME_GE_1_22
    recipe.ResolvedIngredients = ings;
#else
    recipe.resolvedIngredients = ings.Cast<GridRecipeIngredient>().ToArray();
#endif

  private static Block RccBlock(string code, JObject props) {
    var block = TestBlocks.Configure(new Block(), code, 70);
    block.BlockEntityBehaviors =
    [
      new BlockEntityBehaviorType
      {
        Name = "ExRightClickConstructable",
        properties = new JsonObject(props),
      },
    ];
    return block;
  }

  private static JObject Stage(string ingName, int qty) =>
    new() {
      ["requireStacks"] = new JArray
      {
        new JObject { ["name"] = ingName, ["quantity"] = qty },
      },
    };

  private static JArray Stages(Block block) =>
    (JArray)
      ((JObject)block.BlockEntityBehaviors[0].properties!.Token!)["stages"]!;

  private static RecipeProfileCost Grid(params (string code, int qty)[] ings) =>
    new() { Ingredients = ings.ToDictionary(i => i.code, i => i.qty) };

  #region Scale + extract

  [Fact]
  public void A_scaled_profile_reduces_normal_and_floors_each_at_one() {
    var cat = new Dictionary<string, RecipeCostEntry> {
      ["x"] = new() {
        Type = "grid",
        Match = "m:x",
        Profiles = new() { ["normal"] = Grid(("a", 10), ("b", 1)) },
      },
    };

    Assert.True(ExRecipeCosts.EnsureScaledLevel(cat, "cheap", 0.5));

    Assert.Equal(5, cat["x"].Profiles["cheap"].Ingredients!["a"]); // 10 * 0.5
    Assert.Equal(1, cat["x"].Profiles["cheap"].Ingredients!["b"]); // 0.5 -> floored to 1
  }

  [Fact]
  public void A_scaled_profile_never_overwrites_filled_costs() {
    var cat = new Dictionary<string, RecipeCostEntry> {
      ["x"] = new() {
        Type = "grid",
        Match = "m:x",
        Profiles = new() {
          ["normal"] = Grid(("a", 10)),
          ["cheap"] = Grid(("a", 2)), // intentional edit
        },
      },
    };

    ExRecipeCosts.EnsureScaledLevel(cat, "cheap", 0.5);

    Assert.Equal(2, cat["x"].Profiles["cheap"].Ingredients!["a"]);
  }

  [Fact]
  public void A_scaled_profile_fills_ingredients_but_keeps_a_pinned_quantity() {
    var cat = new Dictionary<string, RecipeCostEntry> {
      ["pipe"] = new() {
        Type = "grid",
        Match = "m:pipe",
        Profiles = new() {
          ["normal"] = Grid(("a", 4)),
          ["cheap"] = new() { Quantity = 4 }, // only a pinned output, no ingredients yet
        },
      },
    };

    ExRecipeCosts.EnsureScaledLevel(cat, "cheap", 0.5);

    Assert.Equal(2, cat["pipe"].Profiles["cheap"].Ingredients!["a"]); // filled from normal
    Assert.Equal(4, cat["pipe"].Profiles["cheap"].Quantity); // pin preserved
  }

  [Fact]
  public void Normal_is_extracted_from_the_live_grid_recipe() {
    var world = new TestWorld();
    world.World.GridRecipes.Returns(
      new List<GridRecipe>
      {
        GridRecipe("ppex:enginewatt-north", Ing("game:rod-iron", 8)),
      }
    );

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["watt-grid"] = new() { Type = "grid", Match = "ppex:enginewatt-*" },
    };

    Assert.True(ExRecipeCosts.EnsureNormalExtracted(world.Api, cat));
    Assert.Equal(
      8,
      cat["watt-grid"].Profiles["normal"].Ingredients!["game:rod-iron"]
    );
  }

  [Fact]
  public void Normal_is_extracted_per_stage_from_an_rcc_block() {
    var world = new TestWorld();
    // The same ingredient in two stages stays two separate, per-stage entries.
    var props = new JObject {
      ["stages"] = new JArray { Stage("plate", 4), Stage("plate", 2) },
    };
    world.World.Blocks.Returns(
      new List<Block> { RccBlock("ppex:enginewatt-north", props) }
    );

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["watt-rcc"] = new() { Type = "rcc", Match = "ppex:enginewatt-*" },
    };

    Assert.True(ExRecipeCosts.EnsureNormalExtracted(world.Api, cat));
    var stages = cat["watt-rcc"].Profiles["normal"].Stages!;
    Assert.Equal(4, stages["0"]["plate"]);
    Assert.Equal(2, stages["1"]["plate"]);
  }

  #endregion

  #region Reconcile (robustness against bad edits)

  private static Dictionary<string, RecipeCostEntry> DefaultCat() =>
    new() {
      ["watt-rcc"] = new() {
        Type = "rcc",
        Match = "ppex:enginewatt-*",
        Profiles = new() {
          ["cheap"] = new() {
            Stages = new() { ["1"] = new() { ["plate"] = 2 } },
          },
        },
      },
      ["watt-grid"] = new() { Type = "grid", Match = "ppex:enginewatt-*" },
      ["pipe-grid"] = new() {
        Type = "grid",
        Match = "ppex:pipe-straight-*",
        Profiles = new() { ["cheap"] = new() { Quantity = 4 } },
      },
    };

  [Fact]
  public void Reconcile_restores_a_deleted_entry() {
    var live = new Dictionary<string, RecipeCostEntry>(); // player wiped everything

    Assert.True(ExRecipeCosts.Reconcile(live, DefaultCat()));

    Assert.True(live.ContainsKey("watt-rcc"));
    Assert.Equal("ppex:enginewatt-*", live["watt-rcc"].Match);
    Assert.Equal(2, live["watt-rcc"].Profiles["cheap"].Stages!["1"]["plate"]);
  }

  [Fact]
  public void Reconcile_repairs_structural_fields_and_restores_a_deleted_pinned_profile() {
    var live = new Dictionary<string, RecipeCostEntry> {
      // Player blanked the match/type and deleted the pinned cheap profile.
      ["watt-rcc"] = new() {
        Type = "grid",
        Match = "",
        Profiles = new(),
      },
    };

    Assert.True(ExRecipeCosts.Reconcile(live, DefaultCat()));

    Assert.Equal("rcc", live["watt-rcc"].Type); // restored
    Assert.Equal("ppex:enginewatt-*", live["watt-rcc"].Match); // restored
    Assert.Equal(2, live["watt-rcc"].Profiles["cheap"].Stages!["1"]["plate"]);
  }

  [Fact]
  public void Reconcile_restores_a_deleted_pinned_output_quantity() {
    var live = new Dictionary<string, RecipeCostEntry> {
      // Player kept the entry but dropped the pinned doubled output.
      ["pipe-grid"] = new() {
        Type = "grid",
        Match = "ppex:pipe-straight-*",
        Profiles = new() { ["cheap"] = Grid(("a", 1)) },
      },
    };

    ExRecipeCosts.Reconcile(live, DefaultCat());

    Assert.Equal(4, live["pipe-grid"].Profiles["cheap"].Quantity); // restored
    Assert.Equal(1, live["pipe-grid"].Profiles["cheap"].Ingredients!["a"]); // kept
  }

  [Fact]
  public void Reconcile_clamps_nonsense_quantities_to_at_least_one() {
    var live = new Dictionary<string, RecipeCostEntry> {
      ["watt-grid"] = new() {
        Type = "grid",
        Match = "ppex:enginewatt-*",
        Profiles = new() {
          ["cheap"] = new() {
            Ingredients = new() { ["a"] = 0, ["b"] = -5 },
            Quantity = -2,
          },
        },
      },
    };

    ExRecipeCosts.Reconcile(live, DefaultCat());

    Assert.Equal(1, live["watt-grid"].Profiles["cheap"].Ingredients!["a"]);
    Assert.Equal(1, live["watt-grid"].Profiles["cheap"].Ingredients!["b"]);
    Assert.Equal(1, live["watt-grid"].Profiles["cheap"].Quantity);
  }

  [Fact]
  public void Reconcile_keeps_valid_player_numbers_and_extra_entries() {
    var live = new Dictionary<string, RecipeCostEntry> {
      ["watt-grid"] = new() {
        Type = "grid",
        Match = "ppex:enginewatt-*",
        Profiles = new() { ["cheap"] = Grid(("a", 3)) }, // intentional edit
      },
      ["my-custom"] = new() {
        Type = "grid",
        Match = "mod:thing",
        Profiles = new() { ["cheap"] = Grid(("x", 7)) },
      },
    };

    ExRecipeCosts.Reconcile(live, DefaultCat());

    Assert.Equal(3, live["watt-grid"].Profiles["cheap"].Ingredients!["a"]); // kept
    Assert.True(live.ContainsKey("my-custom")); // extra entry kept
    Assert.Equal(7, live["my-custom"].Profiles["cheap"].Ingredients!["x"]);
  }

  #endregion

  #region Apply

  [Fact]
  public void Applying_a_grid_profile_sets_the_ingredient_quantities() {
    var world = new TestWorld();
    var plate = Ing("game:metalplate-iron", 4);
    var rod = Ing("game:rod-iron", 8);
    world.World.GridRecipes.Returns(
      new List<GridRecipe> { GridRecipe("ppex:enginewatt-north", plate, rod) }
    );

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["watt-grid"] = new() {
        Type = "grid",
        Match = "ppex:enginewatt-*",
        Profiles = new() {
          ["cheap"] = Grid(("game:metalplate-iron", 2), ("game:rod-iron", 4)),
        },
      },
    };

    ExRecipeCosts.Apply(world.Api, cat, "cheap");

    Assert.Equal(2, plate.Quantity);
    Assert.Equal(4, rod.Quantity);
  }

  [Fact]
  public void Applying_a_grid_profile_can_pin_a_doubled_output() {
    var world = new TestWorld();
    var recipe = GridRecipe("ppex:pipe-straight-ns-iron");
    recipe.Output!.Quantity = 2; // authored output
    recipe.Output.ResolvedItemStack = new ItemStack { StackSize = 2 };
    world.World.GridRecipes.Returns(new List<GridRecipe> { recipe });

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["pipe-straight-grid"] = new() {
        Type = "grid",
        Match = "ppex:pipe-straight-*",
        Profiles = new() { ["cheap"] = new() { Quantity = 4 } }, // doubled
      },
    };

    ExRecipeCosts.Apply(world.Api, cat, "cheap");
    Assert.Equal(4, recipe.Output.Quantity);
    Assert.Equal(4, recipe.Output.ResolvedItemStack!.StackSize);

    // A profile with no pinned output leaves the authored count alone.
    recipe.Output.Quantity = 2;
    ExRecipeCosts.Apply(world.Api, cat, "normal");
    Assert.Equal(2, recipe.Output.Quantity);
  }

  [Fact]
  public void Applying_a_grid_profile_also_resizes_the_resolved_stack() {
    // Crafting consumes ResolvedItemStack.StackSize, not Quantity - both must be updated or the
    // recipe keeps charging its original amount (the bug behind grid costs "not changing").
    var world = new TestWorld();
    var rod = Ing("game:rod-iron", 8);
    rod.ResolvedItemStack = new ItemStack { StackSize = 8 };
    world.World.GridRecipes.Returns(
      new List<GridRecipe> { GridRecipe("ppex:enginewatt-north", rod) }
    );

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["watt-grid"] = new() {
        Type = "grid",
        Match = "ppex:enginewatt-*",
        Profiles = new() { ["cheap"] = Grid(("game:rod-iron", 4)) },
      },
    };

    ExRecipeCosts.Apply(world.Api, cat, "cheap");

    Assert.Equal(4, rod.Quantity);
    Assert.Equal(4, rod.ResolvedItemStack!.StackSize);
  }

  [Fact]
  public void Applying_an_rcc_profile_sets_each_stage_independently() {
    var world = new TestWorld();
    // The same ingredient in two stages (4 and 2) is set per stage, not redistributed.
    var props = new JObject {
      ["stages"] = new JArray { Stage("plate", 4), Stage("plate", 2) },
    };
    var block = RccBlock("ppex:enginewatt-north", props);
    world.World.Blocks.Returns(new List<Block> { block });

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["watt-rcc"] = new() {
        Type = "rcc",
        Match = "ppex:enginewatt-*",
        Profiles = new() {
          ["cheap"] = new() {
            Stages = new() {
              ["0"] = new() { ["plate"] = 3 },
              ["1"] = new() { ["plate"] = 1 },
            },
          },
        },
      },
    };

    ExRecipeCosts.Apply(world.Api, cat, "cheap");

    var stages = Stages(block);
    int q0 = (int)stages[0]["requireStacks"]![0]!["quantity"]!;
    int q1 = (int)stages[1]["requireStacks"]![0]!["quantity"]!;
    Assert.Equal(3, q0); // stage 0 set directly
    Assert.Equal(1, q1); // stage 1 set directly - no redistribution from stage 0
  }

  [Fact]
  public void Applying_an_unknown_profile_leaves_recipes_untouched() {
    var world = new TestWorld();
    var rod = Ing("game:rod-iron", 8);
    world.World.GridRecipes.Returns(
      new List<GridRecipe> { GridRecipe("ppex:enginewatt-north", rod) }
    );

    var cat = new Dictionary<string, RecipeCostEntry> {
      ["watt-grid"] = new() {
        Type = "grid",
        Match = "ppex:enginewatt-*",
        Profiles = new() { ["cheap"] = Grid(("game:rod-iron", 4)) },
      },
    };

    ExRecipeCosts.Apply(world.Api, cat, "normal"); // not present -> no-op

    Assert.Equal(8, rod.Quantity);
  }

  #endregion
}

using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using SteelmakingExpanded;
using SteelmakingExpanded.Compat;
using Vintagestory.API.Common;
using Xunit;

namespace SteelmakingExpanded.Tests;

/// <summary>
/// Expanded Matter ships three crushing recipes that all output <c>em:crushed-ore-coal</c> - from
/// charcoal, from coal ore, and from coke. Only the coke one is smex's business. The gate used to be
/// a JSON patch pinning <c>/2/enabled</c>, which is positional: it disabled whatever sat at index 2,
/// and would silently hit a different recipe if EM ever reordered its file.
/// </summary>
public class EmCokeCrushingGateTests
{
  #region Fixture

  private static GridRecipe Recipe(string ingredientDomain, string ingredientPath) =>
    new()
    {
      Output = new CraftingRecipeIngredient
      {
        Code = new AssetLocation("em", "crushed-ore-coal"),
      },
      Ingredients = new Dictionary<string, CraftingRecipeIngredient>
      {
        ["H"] = new() { Code = new AssetLocation("game", "hammer-copper") },
        ["C"] = new()
        {
          Code = new AssetLocation(ingredientDomain, ingredientPath),
        },
      },
    };

  /// <summary>EM's three real crushing recipes, in their shipped order.</summary>
  private static List<GridRecipe> EmRecipes() =>
    [
      Recipe("game", "charcoal"),
      Recipe("game", "ore-bituminouscoal"),
      Recipe("game", "coke"),
    ];

  private static ICoreAPI FakeApi(List<GridRecipe> recipes, bool emInstalled = true)
  {
    var api = Substitute.For<ICoreAPI>();
    api.Logger.Returns(Substitute.For<ILogger>());
    var modLoader = Substitute.For<IModLoader>();
    modLoader.IsModEnabled("em").Returns(emInstalled);
    api.ModLoader.Returns(modLoader);
    var world = Substitute.For<IWorldAccessor>();
    world.GridRecipes.Returns(recipes);
    api.World.Returns(world);
    return api;
  }

  #endregion

  #region Gating

  [Fact]
  public void Removes_the_coke_recipe_and_leaves_ems_own_two_alone()
  {
    var recipes = EmRecipes();
    bool original = SmexValues.EnableEmCokeCrushing;
    try
    {
      SmexValues.Edit(c => c.EnableEmCokeCrushing = false);

      EmCokeCrushingGate.Apply(FakeApi(recipes));

      Assert.Equal(2, recipes.Count);
      Assert.DoesNotContain(
        recipes,
        r => r.Ingredients.Values.Any(i => i.Code?.Path == "coke")
      );
      // EM's charcoal and coal-ore routes are its own economy and must survive.
      Assert.Contains(
        recipes,
        r => r.Ingredients.Values.Any(i => i.Code?.Path == "charcoal")
      );
      Assert.Contains(
        recipes,
        r =>
          r.Ingredients.Values.Any(i =>
            i.Code?.Path == "ore-bituminouscoal"
          )
      );
    }
    finally
    {
      SmexValues.Edit(c => c.EnableEmCokeCrushing = original);
    }
  }

  [Fact]
  public void Keeps_every_recipe_when_the_config_opts_in()
  {
    var recipes = EmRecipes();
    bool original = SmexValues.EnableEmCokeCrushing;
    try
    {
      SmexValues.Edit(c => c.EnableEmCokeCrushing = true);

      EmCokeCrushingGate.Apply(FakeApi(recipes));

      Assert.Equal(3, recipes.Count);
    }
    finally
    {
      SmexValues.Edit(c => c.EnableEmCokeCrushing = original);
    }
  }

  [Fact]
  public void Does_nothing_when_expanded_matter_is_not_installed()
  {
    var recipes = EmRecipes();

    EmCokeCrushingGate.Apply(FakeApi(recipes, emInstalled: false));

    Assert.Equal(3, recipes.Count);
  }

  #endregion
}

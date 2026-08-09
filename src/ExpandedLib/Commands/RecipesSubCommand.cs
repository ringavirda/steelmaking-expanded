using System.Linq;
using ExpandedLib.Registries.Commands;
using ExpandedLib.Registries.Recipes;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Commands;

/// <summary>
/// Adds <c>/exmod recipes [&lt;mod&gt; [&lt;level&gt;]]</c>: the generic, mod-agnostic switch for the
/// recipe-cost levels any mod registers through <see cref="ExRecipeProfiles"/>. With no argument it
/// lists the registered mods and their current level; with a mod code it reports that mod's level; with
/// both it sets the level (e.g. <c>/exmod recipes smex cheap</c>) and persists it. The per-recipe numbers
/// live in each mod's <c>*_recipes.json</c>; the change applies on the next world reload. Server-side,
/// since recipe costs are host-authoritative (the <c>/exmod</c> root requires <c>controlserver</c>).
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class RecipesSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    parent
      .BeginSubCommand("recipes")
      .WithDescription(Lang.Get("exlib:command-recipes-desc"))
      .WithArgs(
        api.ChatCommands.Parsers.OptionalWord("mod"),
        api.ChatCommands.Parsers.OptionalWord("level")
      )
      .HandleWith(OnCommand)
      .EndSubCommand();
  }

  private static TextCommandResult OnCommand(TextCommandCallingArgs args) {
    string? code = (args[0] as string)?.ToLowerInvariant();
    string? level = (args[1] as string)?.ToLowerInvariant();

    if (code == null)
      return TextCommandResult.Success(ListProfiles());

    if (!ExRecipeProfiles.TryGet(code, out var profile))
      return TextCommandResult.Error(
        Lang.Get("exlib:command-recipes-unknown", code, KnownCodes())
      );

    if (level == null)
      return TextCommandResult.Success(
        Lang.Get(
          "exlib:command-recipes-status",
          profile.Code,
          profile.GetLevel()
        )
      );

    if (!profile.Levels.Contains(level))
      return TextCommandResult.Error(
        Lang.Get(
          "exlib:command-recipes-invalid",
          level,
          string.Join(", ", profile.Levels)
        )
      );

    if (level == profile.GetLevel())
      return TextCommandResult.Success(
        Lang.Get("exlib:command-recipes-retain", profile.Code, level)
      );

    profile.SetLevel(level);
    return TextCommandResult.Success(
      Lang.Get("exlib:command-recipes-set", profile.Code, level)
    );
  }

  private static string ListProfiles() {
    if (ExRecipeProfiles.Codes.Count == 0)
      return Lang.Get("exlib:command-recipes-none");

    var lines = ExRecipeProfiles
      .Codes.OrderBy(c => c)
      .Select(c =>
        ExRecipeProfiles.TryGet(c, out var p)
          ? $"  {p.Code}: {p.GetLevel()}"
          : c
      );
    return Lang.Get("exlib:command-recipes-list")
      + "\n"
      + string.Join("\n", lines);
  }

  private static string KnownCodes() =>
    string.Join(", ", ExRecipeProfiles.Codes.OrderBy(c => c));
}

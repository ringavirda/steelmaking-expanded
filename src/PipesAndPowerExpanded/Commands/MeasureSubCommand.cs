using System.Linq;
using ExpandedLib.Registries.Commands;
using ExpandedLib.Registries.Preferences;
using PipesAndPowerExpanded.Patches;
using PipesAndPowerExpanded.Preferences;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PipesAndPowerExpanded.Commands;

/// <summary>
/// Attaches <c>.exmod measure [metric|imperial]</c> to the library's shared <c>.exmod</c> root:
/// shows or (with an argument) sets the per-player display unit system, validating against the
/// preference's options and persisting through <see cref="ExPreferences"/>. The preference itself and
/// its effect on the active display unit system live in <see cref="MeasurePreference"/>. Client-only,
/// since the unit system is a client-side display setting.
/// <para>
/// Per-preference display strings (<c>command-measure-desc</c>, <c>pref-measure-label</c>,
/// <c>pref-measure-{value}</c>) are read from this mod's lang domain; the generic result strings
/// (<c>command-pref-current</c>/<c>-set</c>/<c>-invalid</c>) live in the <c>exlib</c> domain.
/// </para>
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Client)]
public sealed class MeasureSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var capi = (ICoreClientAPI)api;
    var pref = ExPreferences.Find("measure") ?? new MeasurePreference();
    string domain = mod.Info.ModID;

    parent
      .BeginSubCommand(pref.Key)
      .WithDescription(Lang.Get(domain + ":command-" + pref.Key + "-desc"))
      .WithArgs(capi.ChatCommands.Parsers.OptionalWord("value"))
      .HandleWith(args => OnCommand(capi, domain, pref, args))
      .EndSubCommand();
  }

  private static TextCommandResult OnCommand(
    ICoreClientAPI api,
    string domain,
    IExPreference pref,
    TextCommandCallingArgs args
  ) {
    string uid = api.World.Player.PlayerUID;
    string? word = (args[0] as string)?.ToLowerInvariant();
    string label = Lang.Get(domain + ":pref-" + pref.Key + "-label");

    // No argument: report the current setting.
    if (string.IsNullOrEmpty(word))
      return TextCommandResult.Success(
        Lang.Get(
          "exlib:command-pref-current",
          label,
          ValueLabel(domain, pref, ExPreferences.GetForPlayer(uid, pref.Key))
        )
      );

    if (!pref.Options.Contains(word))
      return TextCommandResult.Error(
        Lang.Get(
          "exlib:command-pref-invalid",
          word,
          label,
          string.Join(", ", pref.Options)
        )
      );

    string previous = ExPreferences.GetForPlayer(uid, pref.Key);
    ExPreferences.SetForPlayer(uid, pref.Key, word);

    // Handbook prose is unit-converted only when its pages are built, so a mid-session switch needs
    // the pages rebuilt to re-read the now-active system (the look-at HUD already updates live).
    if (previous != word)
      HandbookUnitPatch.Rebuild(api);

    return TextCommandResult.Success(
      Lang.Get("exlib:command-pref-set", label, ValueLabel(domain, pref, word))
    );
  }

  private static string ValueLabel(
    string domain,
    IExPreference pref,
    string value
  ) => Lang.Get(domain + ":pref-" + pref.Key + "-" + value);
}

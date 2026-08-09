using System.Linq;
using System.Text;
using ExpandedLib.Registries.Commands;
using SteelmakingExpanded.Molds;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace SteelmakingExpanded.Commands;

/// <summary>
/// Attaches <c>/exmod molds &lt;plate|ingot|rod|all&gt; [on|off]</c> to the library's shared server
/// <c>/exmod</c> root (admins only - the root requires the <c>controlserver</c> privilege). With no
/// state it reports each mold's availability; with <c>on</c>/<c>off</c> it flips the matching
/// <see cref="SmexConfig"/> flag through <see cref="MoldGating"/> and persists it. The recipe and
/// handbook changes apply on the next world reload (see <see cref="MoldGating.ApplyDisables"/>);
/// already-placed molds stop casting immediately. Server-side, since the config is host-authoritative.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Server)]
public sealed class MoldsSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    string domain = mod.Info.ModID;
    var parsers = api.ChatCommands.Parsers;

    parent
      .BeginSubCommand("molds")
      .WithDescription(Lang.Get(domain + ":command-molds-desc"))
      .WithArgs(
        parsers.WordRange("type", "plate", "ingot", "rod", "all"),
        parsers.OptionalWordRange("state", "on", "off")
      )
      .HandleWith(args => OnCommand(domain, args))
      .EndSubCommand();
  }

  private static TextCommandResult OnCommand(
    string domain,
    TextCommandCallingArgs args
  ) {
    string type = ((string)args[0]).ToLowerInvariant();
    string? state = (args[1] as string)?.ToLowerInvariant();

    string[] keys = type == "all" ? MoldGating.Keys.ToArray() : [type];

    // No state argument: report the current availability of the requested mold(s).
    if (state == null) {
      var report = new StringBuilder();
      foreach (var key in keys)
        report.AppendLine(StatusLine(domain, key, MoldGating.IsEnabled(key)));
      return TextCommandResult.Success(report.ToString().TrimEnd());
    }

    bool enable = state == "on";
    var result = new StringBuilder();
    foreach (var key in keys) {
      MoldGating.SetEnabled(key, enable);
      result.AppendLine(
        Lang.Get(
          domain + ":command-molds-set",
          MoldName(domain, key),
          StateLabel(domain, enable)
        )
      );
    }
    return TextCommandResult.Success(result.ToString().TrimEnd());
  }

  private static string StatusLine(string domain, string key, bool enabled) =>
    Lang.Get(
      domain + ":command-molds-status",
      MoldName(domain, key),
      StateLabel(domain, enabled)
    );

  private static string MoldName(string domain, string key) =>
    Lang.Get(domain + ":mold-" + key);

  private static string StateLabel(string domain, bool enabled) =>
    Lang.Get(domain + (enabled ? ":mold-enabled" : ":mold-disabled"));
}

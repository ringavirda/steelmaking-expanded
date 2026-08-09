using ExpandedLib.Blocks.Networks;
using ExpandedLib.Registries.Commands;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace ExpandedLib.Commands;

/// <summary>
/// Adds <c>.exmod network hi</c> and <c>.exmod network unhi</c>: toggles the transparent,
/// per-network coloured highlight of every block network (see <see cref="NetworkHighlightModSystem"/>)
/// so a player can see which blocks share a network and where a run is broken. Client-side; the
/// command just flips the toggle and the server (which owns the graph) pushes the highlight.
/// </summary>
[SubCommandRegister(Side = EnumAppSide.Client)]
public sealed class NetworkSubCommand : IExSubCommand {
  public string ParentName => "exmod";

  public void Register(ICoreAPI api, Mod mod, IChatCommand parent) {
    var highlight = api.ModLoader.GetModSystem<NetworkHighlightModSystem>();

    parent
      .BeginSubCommand("network")
      .WithDescription(Lang.Get("exlib:command-network-desc"))
      .BeginSubCommand("hi")
      .WithDescription(Lang.Get("exlib:command-network-hi-desc"))
      .HandleWith(_ => {
        highlight.SetEnabled(true);
        return TextCommandResult.Success(Lang.Get("exlib:network-hi-on"));
      })
      .EndSubCommand()
      .BeginSubCommand("unhi")
      .WithDescription(Lang.Get("exlib:command-network-unhi-desc"))
      .HandleWith(_ => {
        highlight.SetEnabled(false);
        return TextCommandResult.Success(Lang.Get("exlib:network-hi-off"));
      })
      .EndSubCommand()
      .EndSubCommand();
  }
}

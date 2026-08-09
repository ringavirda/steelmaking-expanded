using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Commands;

/// <summary>
/// A chat sub-command that attaches itself to an existing top-level command rather than creating its
/// own. Lets a dependent mod hang options off a shared command (e.g. ppex's <c>measure</c> under the
/// library's <c>.exmod</c> root) without that command having to know its options up front.
/// <para>
/// Each implementation carries a <see cref="CommandRegisterAttribute"/>'s sub-command counterpart
/// (<see cref="SubCommandRegisterAttribute"/>) and is discovered by
/// <see cref="CommandRegistry.RegisterAll"/>, which resolves (or lazily creates) the
/// <see cref="ParentName"/> command and passes it to <see cref="Register"/>. The registry uses the
/// parameterless constructor, so a sub-command should hold no constructor state.
/// </para>
/// </summary>
public interface IExSubCommand {
  /// <summary>Name of the existing top-level command to attach to (e.g. <c>"exmod"</c>). The
  /// registry creates it on first use if no mod has registered it yet.</summary>
  string ParentName { get; }

  /// <summary>
  /// Builds this sub-command onto <paramref name="parent"/> (typically via
  /// <c>parent.BeginSubCommand(...)...EndSubCommand()</c>). Called once per applicable side by
  /// <see cref="CommandRegistry.RegisterAll"/>. For client-only sub-commands
  /// (<see cref="EnumAppSide.Client"/>) <paramref name="api"/> is an
  /// <see cref="Vintagestory.API.Client.ICoreClientAPI"/>; cast as needed.
  /// </summary>
  void Register(ICoreAPI api, Mod mod, IChatCommand parent);
}

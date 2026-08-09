using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Commands;

/// <summary>
/// A self-contained chat command. Each command lives in its own class, carries a
/// <see cref="CommandRegisterAttribute"/> and builds itself against <paramref name="api"/> in
/// <see cref="Register"/> (typically via <c>api.ChatCommands.Create(...)</c>).
/// <para>
/// The registry instantiates the class with its parameterless constructor, so a command should
/// hold no constructor state; capture whatever it needs from <paramref name="api"/> /
/// <paramref name="mod"/> inside <see cref="Register"/>.
/// </para>
/// </summary>
public interface IExCommand {
  /// <summary>
  /// Builds and registers this command. Called once per applicable side by
  /// <see cref="CommandRegistry.RegisterAll"/>. For client-only commands
  /// (<see cref="EnumAppSide.Client"/>) <paramref name="api"/> is an
  /// <see cref="Vintagestory.API.Client.ICoreClientAPI"/>; cast as needed.
  /// </summary>
  void Register(ICoreAPI api, Mod mod);
}

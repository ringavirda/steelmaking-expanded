using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Commands;

/// <summary>
/// Marks an <see cref="IExCommand"/> class for automatic registration by
/// <see cref="CommandRegistry.RegisterAll"/>. Mirrors
/// <see cref="Entities.RegisterAttribute"/>: a command only needs this one
/// attribute and an <see cref="IExCommand.Register"/> body - no hand-written wiring in the
/// mod system.
/// <para>
/// <see cref="Side"/> gates when the command is registered. Display/HUD commands such as
/// <c>.exmod measure</c> are client-only preferences, so they declare
/// <see cref="EnumAppSide.Client"/>; the registry skips them on the other side.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CommandRegisterAttribute : Attribute {
  /// <summary>
  /// The side(s) this command registers on. <see cref="EnumAppSide.Universal"/> (default)
  /// registers on both client and server.
  /// </summary>
  public EnumAppSide Side { get; init; } = EnumAppSide.Universal;
}

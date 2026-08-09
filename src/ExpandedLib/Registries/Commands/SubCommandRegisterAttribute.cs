using System;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Commands;

/// <summary>
/// Marks an <see cref="IExSubCommand"/> class for automatic registration by
/// <see cref="CommandRegistry.RegisterAll"/>. The sub-command counterpart to
/// <see cref="CommandRegisterAttribute"/>: the registry resolves the
/// <see cref="IExSubCommand.ParentName"/> command and lets the class attach itself - no hand-written
/// wiring in the mod system.
/// <para>
/// <see cref="Side"/> gates when the sub-command is registered. Display/HUD options such as
/// <c>.exmod measure</c> are client-only, so they declare <see cref="EnumAppSide.Client"/>; the
/// registry skips them on the other side.
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class SubCommandRegisterAttribute : Attribute {
  /// <summary>
  /// The side(s) this sub-command registers on. <see cref="EnumAppSide.Universal"/> (default)
  /// registers on both client and server.
  /// </summary>
  public EnumAppSide Side { get; init; } = EnumAppSide.Universal;
}

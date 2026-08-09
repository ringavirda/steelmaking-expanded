using System;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Commands;

/// <summary>
/// Reflection-driven chat-command registration for mods built on ExpandedLib, the command-side
/// counterpart to <see cref="Entities.EntityRegistry"/>. Scans an assembly for
/// <see cref="IExCommand"/> classes carrying <see cref="CommandRegisterAttribute"/> (full top-level
/// commands) and <see cref="IExSubCommand"/> classes carrying
/// <see cref="SubCommandRegisterAttribute"/> (options that attach to an existing command), and
/// builds each one - so a mod system never hand-wires <c>api.ChatCommands.Create(...)</c> calls.
/// </summary>
public static class CommandRegistry {
  /// <summary>
  /// Registers every <see cref="CommandRegisterAttribute"/>-decorated <see cref="IExCommand"/> and
  /// every <see cref="SubCommandRegisterAttribute"/>-decorated <see cref="IExSubCommand"/> in
  /// <paramref name="asm"/> (default: the calling mod's own assembly) whose declared side matches
  /// <paramref name="api"/>. Call from <c>ModSystem.Start</c> for universal/server commands and/or
  /// <c>StartClientSide</c> for client commands - each (sub-)command's declared side ensures it only
  /// registers once. Sub-commands resolve their parent through
  /// <see cref="Vintagestory.API.Common.IChatCommandApi.GetOrCreate(string)"/>, so a parent command
  /// need not exist (or be registered by the same mod) beforehand.
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      var attr = type.GetCustomAttribute<CommandRegisterAttribute>();
      if (attr != null) {
        // Universal commands register on whichever side runs; sided ones only on their own side.
        if (attr.Side != EnumAppSide.Universal && attr.Side != api.Side)
          continue;

        if (!typeof(IExCommand).IsAssignableFrom(type)) {
          api.Logger.Warning(
            "[{0}] CommandRegistry: {1} has [CommandRegister] but does not implement IExCommand; skipped.",
            modId,
            type.FullName
          );
          continue;
        }

        var command = (IExCommand)Activator.CreateInstance(type)!;
        command.Register(api, mod);
        continue;
      }

      var subAttr = type.GetCustomAttribute<SubCommandRegisterAttribute>();
      if (subAttr != null) {
        if (subAttr.Side != EnumAppSide.Universal && subAttr.Side != api.Side)
          continue;

        if (!typeof(IExSubCommand).IsAssignableFrom(type)) {
          api.Logger.Warning(
            "[{0}] CommandRegistry: {1} has [SubCommandRegister] but does not implement IExSubCommand; skipped.",
            modId,
            type.FullName
          );
          continue;
        }

        var sub = (IExSubCommand)Activator.CreateInstance(type)!;
        // Resolve the shared parent (creating it if this is the first sub-command to attach).
        IChatCommand parent = api.ChatCommands.GetOrCreate(sub.ParentName);
        sub.Register(api, mod, parent);
      }
    }
  }
}

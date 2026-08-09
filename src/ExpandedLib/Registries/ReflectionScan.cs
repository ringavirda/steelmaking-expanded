using System;
using System.Linq;
using System.Reflection;

namespace ExpandedLib.Registries;

/// <summary>
/// Shared reflection helper for the attribute-driven registries
/// (<see cref="Entities.EntityRegistry"/>, <see cref="Commands.CommandRegistry"/>).
/// </summary>
public static class ReflectionScan {
  /// <summary>
  /// Returns every concrete (non-abstract) class in <paramref name="asm"/>, tolerating a
  /// partial load (<see cref="ReflectionTypeLoadException"/>) so one unloadable type can't
  /// break registration of the rest.
  /// </summary>
  public static Type[] GetCandidateTypes(Assembly asm) {
    try {
      return asm.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray();
    } catch (ReflectionTypeLoadException ex) {
      return ex
        .Types.Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray()!;
    }
  }
}

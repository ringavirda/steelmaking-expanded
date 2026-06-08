using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.EntityRegistry;

/// <summary>
/// Generic, reflection-driven registration for mods built on ExpandedLib. Scans an assembly
/// for types carrying <see cref="EntityRegisterAttribute"/> and registers each with the game
/// under the right registry for its kind (block, item, block entity, behaviour), using the
/// <c>{modid}.{ClassName}</c> naming convention. Replaces the long hand-written list of
/// <c>api.Register*Class</c> calls a mod system would otherwise carry.
/// </summary>
public static class EntityRegistry
{
  /// <summary>
  /// Registers every <see cref="EntityRegisterAttribute"/>-decorated class in
  /// <paramref name="asm"/> (default: the calling mod's own assembly). Call once from
  /// <c>ModSystem.Start</c>.
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null)
  {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    foreach (Type type in GetCandidateTypes(asm))
    {
      var attr = type.GetCustomAttribute<EntityRegisterAttribute>();
      if (attr == null)
        continue;

      string baseKey = attr.Code ?? type.Name;
      string key = attr.PrefixModId ? $"{modId}.{baseKey}" : baseKey;

      if (typeof(Block).IsAssignableFrom(type))
        api.RegisterBlockClass(key, type);
      else if (typeof(Item).IsAssignableFrom(type))
        api.RegisterItemClass(key, type);
      else if (typeof(BlockEntity).IsAssignableFrom(type))
        RegisterBlockEntity(api, modId, key, attr, type);
      else if (typeof(BlockEntityBehavior).IsAssignableFrom(type))
        api.RegisterBlockEntityBehaviorClass(key, type);
      else if (typeof(BlockBehavior).IsAssignableFrom(type))
        api.RegisterBlockBehaviorClass(key, type);
      else if (typeof(CollectibleBehavior).IsAssignableFrom(type))
        api.RegisterCollectibleBehaviorClass(key, type);
      else
        api.Logger.Warning(
          "[{0}] EntityRegistry: {1} has [EntityRegister] but no known base type; skipped.",
          modId,
          type.FullName
        );
    }
  }

  /// <summary>
  /// Registers a block entity under its primary key, plus the short-name aliases
  /// (<c>{modid}.{ShortId}</c>, <c>{ShortId}</c>, <c>{shortid}</c>) for classes named
  /// <c>BlockEntityXxx</c> using the default convention. Aliases are skipped when an explicit
  /// <see cref="EntityRegisterAttribute.Code"/> is given (e.g. a vanilla override).
  /// </summary>
  private static void RegisterBlockEntity(
    ICoreAPI api,
    string modId,
    string key,
    EntityRegisterAttribute attr,
    Type type
  )
  {
    api.RegisterBlockEntityClass(key, type);

    const string prefix = "BlockEntity";
    if (attr.Code != null || !type.Name.StartsWith(prefix))
      return;

    string shortId = type.Name[prefix.Length..];
    api.RegisterBlockEntityClass($"{modId}.{shortId}", type);
    api.RegisterBlockEntityClass(shortId, type);
    api.RegisterBlockEntityClass(shortId.ToLowerInvariant(), type);
  }

  private static Type[] GetCandidateTypes(Assembly asm)
  {
    try
    {
      return asm.GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray();
    }
    catch (ReflectionTypeLoadException ex)
    {
      return ex
        .Types.Where(t => t is { IsClass: true, IsAbstract: false })
        .ToArray()!;
    }
  }
}

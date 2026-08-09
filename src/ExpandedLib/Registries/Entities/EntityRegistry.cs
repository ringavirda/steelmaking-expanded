using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Common;

namespace ExpandedLib.Registries.Entities;

/// <summary>
/// Generic, reflection-driven registration for mods built on ExpandedLib. Scans an assembly for
/// types carrying a <see cref="RegisterAttribute"/> (one of the kind-specific
/// <c>[BlockRegister]</c>, <c>[ItemRegister]</c>, <c>[BlockEntityRegister]</c>,
/// <c>[BlockBehaviorRegister]</c>, <c>[BlockEntityBehaviorRegister]</c>,
/// <c>[CollectibleBehaviorRegister]</c> attributes) and registers each with the game under the
/// matching registry, using the <c>{modid}.{ClassName}</c> naming convention. Replaces the long
/// hand-written list of <c>api.Register*Class</c> calls a mod system would otherwise carry.
/// </summary>
public static class EntityRegistry {
  /// <summary>
  /// Registers every <see cref="RegisterAttribute"/>-decorated class in <paramref name="asm"/>
  /// (default: the calling mod's own assembly). Call once from <c>ModSystem.Start</c>.
  /// </summary>
  public static void RegisterAll(ICoreAPI api, Mod mod, Assembly? asm = null) {
    asm ??= Assembly.GetCallingAssembly();
    string modId = mod.Info.ModID;

    foreach (Type type in ReflectionScan.GetCandidateTypes(asm)) {
      var attr = type.GetCustomAttributes()
        .OfType<RegisterAttribute>()
        .FirstOrDefault();
      if (attr == null)
        continue;

      string baseKey = attr.Code ?? type.Name;
      string key = attr.PrefixModId ? $"{modId}.{baseKey}" : baseKey;

      switch (attr) {
        case BlockRegisterAttribute
          when Validate<Block>(api, modId, type, "block"):
          api.RegisterBlockClass(key, type);
          break;
        case ItemRegisterAttribute
          when Validate<Item>(api, modId, type, "item"):
          api.RegisterItemClass(key, type);
          break;
        case BlockEntityRegisterAttribute
          when Validate<BlockEntity>(api, modId, type, "block entity"):
          RegisterBlockEntity(api, modId, key, attr, type);
          break;
        case BlockBehaviorRegisterAttribute
          when Validate<BlockBehavior>(api, modId, type, "block behavior"):
          api.RegisterBlockBehaviorClass(key, type);
          break;
        case BlockEntityBehaviorRegisterAttribute
          when Validate<BlockEntityBehavior>(
            api,
            modId,
            type,
            "block entity behavior"
          ):
          api.RegisterBlockEntityBehaviorClass(key, type);
          break;
        case CollectibleBehaviorRegisterAttribute
          when Validate<CollectibleBehavior>(
            api,
            modId,
            type,
            "collectible behavior"
          ):
          api.RegisterCollectibleBehaviorClass(key, type);
          break;
      }
    }
  }

  /// <summary>Logs a warning and returns false when <paramref name="type"/> does not derive from the
  /// base type its register attribute implies (a mis-applied attribute), so it is skipped rather than
  /// throwing inside the game's registry.</summary>
  private static bool Validate<TBase>(
    ICoreAPI api,
    string modId,
    Type type,
    string kind
  ) {
    if (typeof(TBase).IsAssignableFrom(type))
      return true;

    api.Logger.Warning(
      "[{0}] EntityRegistry: {1} is marked as a {2} but does not derive from {3}; skipped.",
      modId,
      type.FullName,
      kind,
      typeof(TBase).Name
    );
    return false;
  }

  /// <summary>
  /// Registers a block entity under its primary key, plus the short-name aliases
  /// (<c>{modid}.{ShortId}</c>, <c>{ShortId}</c>, <c>{shortid}</c>) for classes named
  /// <c>BlockEntityXxx</c> using the default convention. Aliases are skipped when an explicit
  /// <see cref="RegisterAttribute.Code"/> is given (e.g. a vanilla override).
  /// </summary>
  private static void RegisterBlockEntity(
    ICoreAPI api,
    string modId,
    string key,
    RegisterAttribute attr,
    Type type
  ) {
    api.RegisterBlockEntityClass(key, type);

    const string prefix = "BlockEntity";
    if (attr.Code != null || !type.Name.StartsWith(prefix))
      return;

    string shortId = type.Name[prefix.Length..];
    api.RegisterBlockEntityClass($"{modId}.{shortId}", type);
    api.RegisterBlockEntityClass(shortId, type);
    api.RegisterBlockEntityClass(shortId.ToLowerInvariant(), type);
  }
}

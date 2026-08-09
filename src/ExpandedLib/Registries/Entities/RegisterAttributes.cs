using System;

namespace ExpandedLib.Registries.Entities;

/// <summary>
/// Base for the kind-specific registration attributes (<see cref="BlockRegisterAttribute"/>,
/// <see cref="ItemRegisterAttribute"/>, <see cref="BlockEntityRegisterAttribute"/>,
/// <see cref="BlockBehaviorRegisterAttribute"/>, <see cref="BlockEntityBehaviorRegisterAttribute"/>,
/// <see cref="CollectibleBehaviorRegisterAttribute"/>). A class carries exactly one;
/// <see cref="EntityRegistry.RegisterAll"/> scans for these and registers each under the matching
/// game registry, validating that the class's base type matches the attribute's kind.
/// <para>
/// By default the registry key is <c>{modid}.{ClassName}</c>. Set <see cref="Code"/> and
/// <see cref="PrefixModId"/> to register under an explicit / bare key (e.g. when replacing a vanilla
/// class: <c>[BlockBehaviorRegister("MultiblockStructure", PrefixModId = false)]</c>).
/// </para>
/// </summary>
public abstract class RegisterAttribute(string? code = null) : Attribute {
  /// <summary>Explicit registry key. When null, the class name is used.</summary>
  public string? Code { get; } = code;

  /// <summary>When true (default) the key is prefixed with <c>{modid}.</c>. Set false to register
  /// under a bare key, as required when replacing a vanilla class.</summary>
  public bool PrefixModId { get; init; } = true;
}

/// <summary>Registers a <see cref="Vintagestory.API.Common.Block"/> class. Blocks are singletons
/// (one instance per variant), so their JSON attributes can be surfaced as generated members.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers an <see cref="Vintagestory.API.Common.Item"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ItemRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.BlockEntity"/> class. Classes named
/// <c>BlockEntityXxx</c> under the default convention also gain the short-name aliases.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockEntityRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.BlockBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.BlockEntityBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class BlockEntityBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

/// <summary>Registers a <see cref="Vintagestory.API.Common.CollectibleBehavior"/> class.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CollectibleBehaviorRegisterAttribute(string? code = null)
  : RegisterAttribute(code);

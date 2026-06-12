using System;

namespace ExpandedLib.EntityRegistry;

/// <summary>
/// Marks a block / block-entity / item / behaviour class for automatic registration by
/// <see cref="EntityRegistry.RegisterAll"/>. The registry kind is inferred from the
/// class's base type, so a class only needs this one attribute — no manual
/// <c>api.Register*Class</c> call in the mod system.
/// <para>
/// By default the registry key is <c>{modid}.{ClassName}</c> and (for block entities whose
/// name starts with <c>BlockEntity</c>) the short-name aliases are also registered, matching
/// the long-standing convention. Set <see cref="Code"/> and <see cref="PrefixModId"/> to
/// override a vanilla class instead (e.g. <c>[EntityRegister("CoalPile", PrefixModId = false)]</c>).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EntityRegisterAttribute : Attribute
{
  /// <summary>Explicit registry key. When null, the class name is used.</summary>
  public string? Code { get; }

  /// <summary>
  /// When true (default) the key is prefixed with <c>{modid}.</c>. Set false to register
  /// under a bare key, as required when replacing a vanilla class.
  /// </summary>
  public bool PrefixModId { get; init; } = true;

  public EntityRegisterAttribute(string? code = null) => Code = code;
}

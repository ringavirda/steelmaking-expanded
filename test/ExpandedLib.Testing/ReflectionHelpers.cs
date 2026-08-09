using System;
using System.Reflection;

namespace ExpandedLib.Testing;

/// <summary>
/// Small reflection shims for poking at production members that are not publicly settable but
/// must be primed for a headless test (e.g. the network manager's server-world back-reference,
/// normally assigned only inside <c>StartServerSide</c>).
/// </summary>
public static class ReflectionHelpers {
  /// <summary>Sets a property's value through its (possibly non-public) setter.</summary>
  public static void SetProperty(
    object target,
    string propertyName,
    object? value
  ) {
    PropertyInfo prop =
      target
        .GetType()
        .GetProperty(
          propertyName,
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        )
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' not found on {target.GetType().Name}."
      );

    MethodInfo setter =
      prop.GetSetMethod(nonPublic: true)
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' on {target.GetType().Name} has no setter."
      );

    setter.Invoke(target, [value]);
  }

  /// <summary>Reads a property's value through its (possibly non-public) getter.</summary>
  public static object? GetProperty(object target, string propertyName) {
    PropertyInfo prop =
      target
        .GetType()
        .GetProperty(
          propertyName,
          BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
        )
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' not found on {target.GetType().Name}."
      );

    MethodInfo getter =
      prop.GetGetMethod(nonPublic: true)
      ?? throw new InvalidOperationException(
        $"Property '{propertyName}' on {target.GetType().Name} has no getter."
      );

    return getter.Invoke(target, []);
  }

  /// <summary>Sets a (possibly non-public) instance field, walking up the type hierarchy so a field
  /// declared on a base class is found from a derived instance.</summary>
  public static void SetField(object target, string fieldName, object? value) =>
    FindField(target.GetType(), fieldName).SetValue(target, value);

  /// <summary>Reads a (possibly non-public) instance field, walking up the type hierarchy.</summary>
  public static object? GetField(object target, string fieldName) =>
    FindField(target.GetType(), fieldName).GetValue(target);

  /// <summary>
  /// Invokes a (possibly non-public) method on <paramref name="target"/>, walking up the type
  /// hierarchy. Static methods are found too, and called with a null receiver - a rule expressed as
  /// a static helper is still that type's rule, and a test should not have to make it an instance
  /// method to reach it.
  /// </summary>
  public static object? Invoke(
    object target,
    string methodName,
    params object?[] args
  ) {
    for (Type? t = target.GetType(); t != null; t = t.BaseType) {
      MethodInfo? m = t.GetMethod(
        methodName,
        BindingFlags.Public
          | BindingFlags.NonPublic
          | BindingFlags.Instance
          | BindingFlags.Static
          | BindingFlags.DeclaredOnly
      );
      if (m != null)
        return m.Invoke(m.IsStatic ? null : target, args);
    }
    throw new InvalidOperationException(
      $"Method '{methodName}' not found on {target.GetType().Name}."
    );
  }

  private static FieldInfo FindField(Type type, string fieldName) {
    for (Type? t = type; t != null; t = t.BaseType) {
      FieldInfo? f = t.GetField(
        fieldName,
        BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance
      );
      if (f != null)
        return f;
    }
    throw new InvalidOperationException(
      $"Field '{fieldName}' not found on {type.Name}."
    );
  }
}

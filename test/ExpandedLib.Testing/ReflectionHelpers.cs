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

  /// <summary>Sets a (possibly non-public) instance field, walking up the type hierarchy so a field
  /// declared on a base class is found from a derived instance.</summary>
  public static void SetField(object target, string fieldName, object? value) =>
    FindField(target.GetType(), fieldName).SetValue(target, value);

  /// <summary>Reads a (possibly non-public) instance field, walking up the type hierarchy.</summary>
  public static object? GetField(object target, string fieldName) =>
    FindField(target.GetType(), fieldName).GetValue(target);

  /// <summary>Invokes a (possibly non-public) instance method, walking up the type hierarchy.</summary>
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
          | BindingFlags.DeclaredOnly
      );
      if (m != null)
        return m.Invoke(target, args);
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

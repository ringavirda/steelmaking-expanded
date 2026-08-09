using System.Collections.Generic;

namespace ExpandedLib.Registries.Config;

/// <summary>
/// Non-generic view over a config store (<see cref="ExConfigRegister{TConfig}"/>) that the generic
/// <c>/exmod config</c> command uses to list, read and set a mod's gameplay tunables by name without
/// knowing the concrete config type. A mod opts a config in by marking it
/// <c>[ExConfigRegister(..., Manageable = true)]</c>; the generated accessor then registers its store
/// with <see cref="ExConfigProfiles"/> at load.
/// </summary>
public interface IExConfigAccess {
  /// <summary>The owning mod id, e.g. <c>"smex"</c> - the code typed in <c>/exmod config smex ...</c>.</summary>
  string ModId { get; }

  /// <summary>The config file this store reads/writes, for display.</summary>
  string FileName { get; }

  /// <summary>The names of every editable simple-typed value (numbers, booleans, strings), in
  /// declaration order. Excludes the version stamp and complex/collection values.</summary>
  IReadOnlyList<string> ValueNames { get; }

  /// <summary>Looks up a value by name (case-insensitive) and formats its current value, returning the
  /// config's canonical casing via <paramref name="canonicalName"/>. <c>false</c> if no such value.</summary>
  bool TryGet(string name, out string canonicalName, out string value);

  /// <summary>Parses <paramref name="raw"/> into the named value's type, validates it, sets it on the
  /// live config and persists the file - so the change applies immediately to everything reading the
  /// value through the accessor's getters. Returns the outcome (incl. canonical name and old/new value).</summary>
  ExConfigEditResult Set(string name, string raw);
}

/// <summary>Outcome category of an <see cref="IExConfigAccess.Set"/> attempt.</summary>
public enum ExConfigEditStatus {
  /// <summary>The value was parsed, validated, set and persisted.</summary>
  Ok,

  /// <summary>No editable value by that name exists in the config.</summary>
  UnknownValue,

  /// <summary>The supplied text could not be parsed into the value's type.</summary>
  ParseFailed,

  /// <summary>The parsed value is out of range (a NaN/infinite/negative number).</summary>
  OutOfRange,
}

/// <summary>The result of an attempted config edit, with enough detail for the command to report it.</summary>
public sealed class ExConfigEditResult {
  /// <summary>What happened.</summary>
  public required ExConfigEditStatus Status { get; init; }

  /// <summary>The config's canonical name for the value (when resolved), else the supplied name.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The value before the edit (when the value was resolved).</summary>
  public string? OldValue { get; init; }

  /// <summary>The value after a successful edit.</summary>
  public string? NewValue { get; init; }

  /// <summary>For <see cref="ExConfigEditStatus.ParseFailed"/>: a short name of the expected input
  /// (e.g. <c>"number"</c>, <c>"true/false"</c>).</summary>
  public string? Expected { get; init; }

  /// <summary>For <see cref="ExConfigEditStatus.OutOfRange"/>: the accepted range, e.g. <c>"0 to 1"</c>.</summary>
  public string? Range { get; init; }
}

using System;

namespace ExpandedLib.Registries.Config;

/// <summary>
/// Marks a config POCO (one implementing <see cref="IExVersionedConfig"/>) for which the
/// <c>ExConfigGenerator</c> source generator emits a static accessor class - the
/// <c>const ConfigFileName</c>, the backing <see cref="ExConfigRegister{TConfig}"/>, a
/// <c>Load(ICoreAPI)</c> method and one read-only <c>public static</c> property per config value -
/// so the accessors never have to be hand-maintained alongside the config fields.
/// <para>
/// The generated class is <c>static partial</c> and lives in the config's namespace, so extra
/// members can be added in a hand-written partial. If the config declares a <c>public static</c>
/// <c>Migrations</c> member it is forwarded to the store automatically.
/// </para>
/// </summary>
/// <example>
/// <code>
/// [ExConfig("ppex.json", "ppex")]
/// public class PpexConfig : IExVersionedConfig { /* properties */ }
/// // generates: PpexValues.Load(api), PpexValues.LitresPerPipe, ...
/// </code>
/// </example>
[AttributeUsage(
  AttributeTargets.Class,
  AllowMultiple = false,
  Inherited = false
)]
public sealed class ExConfigRegisterAttribute : Attribute {
  /// <param name="fileName">Config file name under the game's <c>ModConfig</c> folder (e.g. <c>"ppex.json"</c>).</param>
  /// <param name="modId">The owning mod id, used to resolve the running version and tag log lines.</param>
  public ExConfigRegisterAttribute(string fileName, string modId) {
    FileName = fileName;
    ModId = modId;
  }

  /// <summary>Config file name under the game's <c>ModConfig</c> folder.</summary>
  public string FileName { get; }

  /// <summary>The owning mod id.</summary>
  public string ModId { get; }

  /// <summary>Name of the generated accessor class. Defaults to the config type name with a trailing
  /// <c>Config</c> swapped for <c>Values</c> (e.g. <c>PpexConfig</c> → <c>PpexValues</c>), or the type
  /// name plus <c>Values</c> when it has no <c>Config</c> suffix.</summary>
  public string? AccessorName { get; set; }

  /// <summary>Former names this config file used under <c>ModConfig</c>. On load, if the current
  /// <see cref="FileName"/> is absent but one of these still exists, it is renamed to the current name
  /// (first match wins) - so renaming a config in a new release carries the player's existing tuning
  /// over instead of silently regenerating defaults.</summary>
  public string[]? LegacyFileNames { get; set; }

  /// <summary>When <c>true</c>, the generated accessor registers this config's store with
  /// <see cref="ExpandedLib.Registries.Config.ExConfigProfiles"/> at load, exposing its simple-typed
  /// values to the generic <c>/exmod config &lt;mod&gt; &lt;value&gt; [&lt;new&gt;]</c> command. Use it
  /// for the mod's tunables config; leave it off for catalogue configs (e.g. recipe costs) that have
  /// their own command.</summary>
  public bool Manageable { get; set; }
}

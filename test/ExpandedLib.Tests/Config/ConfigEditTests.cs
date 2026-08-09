using System.IO;
using System.Linq;
using ExpandedLib.Registries.Config;
using NSubstitute;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Xunit;

namespace ExpandedLib.Tests;

/// <summary>A config POCO spanning every editable value type plus a non-editable complex one, so the
/// store's value-listing and parse/format paths are all exercised.</summary>
internal sealed class EditableConfig : IExVersionedConfig {
  public string? ConfigVersion { get; set; }
  public int Count { get; set; } = 10;
  public long Big { get; set; } = 1000;
  public float Rate { get; set; } = 1.5f;
  public double Precise { get; set; } = 2.25d;
  public bool Enabled { get; set; } = true;
  public string Label { get; set; } = "normal";

  /// <summary>A bounded fraction: only values in [0, 1] are accepted.</summary>
  [ExConfigRange(0, 1)]
  public float Ratio { get; set; } = 0.5f;

  /// <summary>Complex type: must be excluded from the editable value set.</summary>
  public int[] NotEditable { get; set; } = [1, 2, 3];

  /// <summary>Read-only: must be excluded too.</summary>
  public int Derived => Count * 2;
}

/// <summary>
/// The runtime config-editing path the generic <c>/exmod config</c> command drives through
/// <see cref="IExConfigAccess"/>: which values are exposed, reading and formatting them, and parsing /
/// validating / setting new ones - plus the legacy-file rename that carries a player's tuning over a
/// config rename.
/// </summary>
public class ConfigEditTests {
  private static ExConfigRegister<EditableConfig> Store() =>
    new("editable.json", "fakemod");

  #region Value listing
  [Fact]
  public void ValueNames_lists_only_simple_read_write_values() {
    IExConfigAccess store = Store();

    Assert.Equal(
      ["Count", "Big", "Rate", "Precise", "Enabled", "Label", "Ratio"],
      store.ValueNames.ToArray()
    );
  }
  #endregion

  #region Reading values
  [Fact]
  public void TryGet_is_case_insensitive_and_returns_canonical_name() {
    IExConfigAccess store = Store();

    bool found = store.TryGet("rate", out string name, out string value);

    Assert.True(found);
    Assert.Equal("Rate", name); // canonical casing from the config
    Assert.Equal("1.5", value); // invariant formatting
  }

  [Fact]
  public void TryGet_formats_bool_as_lowercase_word() {
    IExConfigAccess store = Store();

    store.TryGet("Enabled", out _, out string value);

    Assert.Equal("true", value);
  }

  [Fact]
  public void TryGet_returns_false_for_unknown_or_non_editable_value() {
    IExConfigAccess store = Store();

    Assert.False(store.TryGet("nope", out _, out _));
    Assert.False(store.TryGet("NotEditable", out _, out _)); // complex type
    Assert.False(store.TryGet("Derived", out _, out _)); // read-only
    Assert.False(store.TryGet("ConfigVersion", out _, out _)); // version stamp
  }
  #endregion

  #region Setting values
  [Fact]
  public void Set_parses_and_applies_each_value_type() {
    var store = Store();

    Assert.Equal(ExConfigEditStatus.Ok, store.Set("count", "42").Status);
    Assert.Equal(ExConfigEditStatus.Ok, store.Set("rate", "3.75").Status);
    Assert.Equal(ExConfigEditStatus.Ok, store.Set("enabled", "off").Status);
    Assert.Equal(ExConfigEditStatus.Ok, store.Set("label", "cheap").Status);

    Assert.Equal(42, store.Config.Count);
    Assert.Equal(3.75f, store.Config.Rate, 3);
    Assert.False(store.Config.Enabled);
    Assert.Equal("cheap", store.Config.Label);
  }

  [Fact]
  public void Set_reports_old_and_new_value_and_canonical_name() {
    var store = Store();

    var result = store.Set("RATE", "9");

    Assert.Equal(ExConfigEditStatus.Ok, result.Status);
    Assert.Equal("Rate", result.Name);
    Assert.Equal("1.5", result.OldValue);
    Assert.Equal("9", result.NewValue);
  }

  [Theory]
  [InlineData("on", true)]
  [InlineData("1", true)]
  [InlineData("yes", true)]
  [InlineData("false", false)]
  [InlineData("0", false)]
  [InlineData("no", false)]
  public void Set_accepts_lenient_boolean_words(string raw, bool expected) {
    var store = Store();

    Assert.Equal(ExConfigEditStatus.Ok, store.Set("enabled", raw).Status);
    Assert.Equal(expected, store.Config.Enabled);
  }

  [Fact]
  public void Set_rejects_unparseable_input_without_changing_the_value() {
    var store = Store();

    var result = store.Set("count", "lots");

    Assert.Equal(ExConfigEditStatus.ParseFailed, result.Status);
    Assert.Equal(10, store.Config.Count); // unchanged
  }

  [Fact]
  public void Set_rejects_negative_number_as_out_of_range() {
    var store = Store();

    var result = store.Set("rate", "-1");

    Assert.Equal(ExConfigEditStatus.OutOfRange, result.Status);
    Assert.Equal(1.5f, store.Config.Rate, 3); // unchanged
  }

  [Theory]
  [InlineData("1.5")] // above the max
  [InlineData("-0.1")] // below the min
  public void Set_rejects_a_value_outside_its_declared_range(string raw) {
    var store = Store();

    var result = store.Set("ratio", raw);

    Assert.Equal(ExConfigEditStatus.OutOfRange, result.Status);
    Assert.Equal("0..1", result.Range); // surfaced to the player
    Assert.Equal(0.5f, store.Config.Ratio, 3); // unchanged
  }

  [Theory]
  [InlineData("0")]
  [InlineData("1")]
  [InlineData("0.75")]
  public void Set_accepts_a_value_within_its_declared_range(string raw) {
    var store = Store();

    Assert.Equal(ExConfigEditStatus.Ok, store.Set("ratio", raw).Status);
  }

  [Fact]
  public void Set_reports_the_non_negative_baseline_range_for_an_unbounded_value() {
    var store = Store();

    var result = store.Set("rate", "-1");

    Assert.Equal("0+", result.Range); // no upper bound -> floor only
  }

  [Fact]
  public void Set_returns_unknown_for_a_missing_value() {
    var store = Store();

    Assert.Equal(
      ExConfigEditStatus.UnknownValue,
      store.Set("nope", "1").Status
    );
  }
  #endregion

  #region Load-time sanitization
  [Fact]
  public void Load_resets_out_of_range_and_invalid_values_to_defaults() {
    using var dir = new TempModConfig();
    var bad = new EditableConfig {
      Ratio = 5f, // out of [0, 1]
      Count = -1, // negative (baseline guard)
      Rate = float.NaN, // not finite
    };

    var store = new ExConfigRegister<EditableConfig>("c.json", "fakemod");
    store.Load(FakeApiLoading(bad));

    Assert.Equal(0.5f, store.Config.Ratio, 3); // reset to default
    Assert.Equal(10, store.Config.Count); // reset to default
    Assert.Equal(1.5f, store.Config.Rate, 3); // reset to default
  }

  [Fact]
  public void Load_keeps_in_range_values() {
    using var dir = new TempModConfig();
    var good = new EditableConfig { Ratio = 0.9f, Count = 7 };

    var store = new ExConfigRegister<EditableConfig>("c.json", "fakemod");
    store.Load(FakeApiLoading(good));

    Assert.Equal(0.9f, store.Config.Ratio, 3); // within [0, 1] - kept
    Assert.Equal(7, store.Config.Count);
  }
  #endregion

  #region Legacy file rename
  [Fact]
  public void Load_renames_a_present_legacy_file_to_the_current_name() {
    using var dir = new TempModConfig();
    File.WriteAllText(dir.Path("old.json"), "{}");

    var store = new ExConfigRegister<EditableConfig>("new.json", "fakemod") {
      LegacyFileNames = ["old.json"],
    };
    store.Load(FakeApi());

    Assert.True(File.Exists(dir.Path("new.json")));
    Assert.False(File.Exists(dir.Path("old.json")));
  }

  [Fact]
  public void Load_leaves_legacy_file_alone_when_current_file_exists() {
    using var dir = new TempModConfig();
    File.WriteAllText(dir.Path("old.json"), "{}");
    File.WriteAllText(dir.Path("new.json"), "{}");

    var store = new ExConfigRegister<EditableConfig>("new.json", "fakemod") {
      LegacyFileNames = ["old.json"],
    };
    store.Load(FakeApi());

    Assert.True(File.Exists(dir.Path("old.json"))); // untouched
  }

  [Fact]
  public void Load_with_no_legacy_names_is_a_noop() {
    using var dir = new TempModConfig();

    var store = new ExConfigRegister<EditableConfig>("new.json", "fakemod");
    store.Load(FakeApi()); // must not throw or create anything

    Assert.False(File.Exists(dir.Path("new.json")));
  }
  #endregion

  #region Unreadable file is preserved, not overwritten

  // A config the player edited by hand is their data. Every path that fails to reproduce their
  // values used to end in an unconditional rewrite from defaults, which is how an edited value
  // "reverts on its own" and then sticks after a second edit.

  [Fact]
  public void Load_backs_up_a_file_that_cannot_be_parsed() {
    using var dir = new TempModConfig();
    File.WriteAllText(dir.Path("new.json"), "{ this is not json");

    var api = FakeApiLoading(null);
    api.LoadModConfig<EditableConfig>(Arg.Any<string>())
      .Returns(_ => throw new System.Exception("bad json"));

    new ExConfigRegister<EditableConfig>("new.json", "fakemod").Load(api);

    Assert.True(
      File.Exists(dir.Path("new.json.corrupt")),
      "the unreadable file should be preserved"
    );
    Assert.Equal(
      "{ this is not json",
      File.ReadAllText(dir.Path("new.json.corrupt"))
    );
  }

  [Fact]
  public void Load_backs_up_a_present_but_blank_file() {
    using var dir = new TempModConfig();
    // Whitespace deserializes to null rather than throwing, so this never reached the error path.
    File.WriteAllText(dir.Path("new.json"), "   \n  ");

    new ExConfigRegister<EditableConfig>("new.json", "fakemod").Load(FakeApi());

    Assert.True(File.Exists(dir.Path("new.json.corrupt")));
  }

  // Control: a first run has no file at all, which is normal and must not produce a backup.
  [Fact]
  public void Load_does_not_back_up_when_no_file_exists_yet() {
    using var dir = new TempModConfig();

    new ExConfigRegister<EditableConfig>("new.json", "fakemod").Load(FakeApi());

    Assert.False(File.Exists(dir.Path("new.json.corrupt")));
  }

  [Fact]
  public void Load_does_not_write_the_shared_file_from_the_client() {
    using var dir = new TempModConfig();
    var api = FakeApiLoading(null);
    api.Side.Returns(EnumAppSide.Client);

    new ExConfigRegister<EditableConfig>("new.json", "fakemod").Load(api);

    // In singleplayer both sides load the same file; only the server may own it.
    api.DidNotReceive()
      .StoreModConfig(Arg.Any<EditableConfig>(), Arg.Any<string>());
  }

  [Fact]
  public void Load_still_writes_the_file_from_the_server() {
    using var dir = new TempModConfig();
    var api = FakeApiLoading(null);
    api.Side.Returns(EnumAppSide.Server);

    new ExConfigRegister<EditableConfig>("new.json", "fakemod").Load(api);

    api.Received().StoreModConfig(Arg.Any<EditableConfig>(), Arg.Any<string>());
  }

  #endregion

  /// <summary>A fake API whose <c>LoadModConfig</c> returns null (so Load falls back to defaults) and
  /// whose mod version resolves; the legacy rename runs against the real filesystem via GamePaths.</summary>
  private static ICoreAPI FakeApi() => FakeApiLoading(null);

  /// <summary>As <see cref="FakeApi"/>, but <c>LoadModConfig</c> returns <paramref name="loaded"/> - so a
  /// test can feed a tampered-with config into <see cref="ExConfigRegister{TConfig}.Load"/>.</summary>
  private static ICoreAPI FakeApiLoading(EditableConfig? loaded) {
    var api = Substitute.For<ICoreAPI>();
    api.Logger.Returns(Substitute.For<ILogger>());
    api.LoadModConfig<EditableConfig>(Arg.Any<string>()).Returns(loaded);

    var mod = Substitute.For<Mod>();
    typeof(Mod)
      .GetProperty("Info")!
      .SetValue(mod, new ModInfo { Version = "1.0.0" });
    var modLoader = Substitute.For<IModLoader>();
    modLoader.GetMod("fakemod").Returns(mod);
    api.ModLoader.Returns(modLoader);

    return api;
  }

  /// <summary>Points <see cref="GamePaths.ModConfig"/> at a throwaway temp folder for a test and
  /// removes it afterwards, so the rename runs against a real - but disposable - directory.</summary>
  private sealed class TempModConfig : System.IDisposable {
    private readonly string _root = System.IO.Path.Combine(
      System.IO.Path.GetTempPath(),
      "exlib_cfgtest_" + System.Guid.NewGuid().ToString("N")
    );

    public TempModConfig() {
      GamePaths.DataPath = _root;
      Directory.CreateDirectory(GamePaths.ModConfig);
    }

    public string Path(string file) =>
      System.IO.Path.Combine(GamePaths.ModConfig, file);

    public void Dispose() {
      try {
        Directory.Delete(_root, recursive: true);
      } catch { /* best-effort cleanup */
      }
    }
  }
}

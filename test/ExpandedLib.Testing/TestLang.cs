using NSubstitute;
using Vintagestory.API.Config;

namespace ExpandedLib.Testing;

/// <summary>
/// Stands up a minimal headless <see cref="Lang"/> so production code that formats player-facing
/// strings (block info, HUD, measurement units) can call <see cref="Lang.Get"/> without the game's
/// asset pipeline. Without this, <c>Lang.Get</c> throws because <c>CurrentLocale</c> is null and the
/// language dictionary is empty.
/// <para>
/// The registered translation service simply echoes the key back (the same graceful fallback the
/// real service uses for an untranslated key), which is all tests need: assertions check the numeric
/// payload of a formatted string, not the localized unit label.
/// </para>
/// </summary>
public static class TestLang {
  private static bool _ready;

  /// <summary>Idempotently registers an echo-the-key translation service for the "en" locale.</summary>
  public static void Init() {
    if (_ready)
      return;
    _ready = true;

    var svc = Substitute.For<ITranslationService>();
    svc.LanguageCode.Returns("en");
    svc.Get(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.GetIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.GetUnformatted(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
    svc.GetMatching(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.GetMatchingIfExists(Arg.Any<string>(), Arg.Any<object[]>())
      .Returns(ci => ci.Arg<string>());
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>()).Returns(true);
    svc.HasTranslation(Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<bool>())
      .Returns(true);

    Lang.DefaultLocale = "en";
    // AvailableLanguages is a read-only auto-property over a dictionary the engine mutates in
    // place (Load adds to it); add our locale the same way rather than replacing the field.
    Lang.AvailableLanguages["en"] = svc;
    // ChangeLanguage is the engine's path to set the otherwise-getter-only CurrentLocale.
    Lang.ChangeLanguage("en");
  }
}

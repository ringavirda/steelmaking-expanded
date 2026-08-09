// Polyfill so C# records/`init` accessors compile on netstandard2.0 (the framework analyzers
// load under), which predates this type.
namespace System.Runtime.CompilerServices {
  internal static class IsExternalInit { }
}

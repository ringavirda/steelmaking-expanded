using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using ExpandedLib.Generators.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace ExpandedLib.Generators;

/// <summary>
/// Emits a <c>partial</c> half for every <c>[BlockRegister]</c> / <c>[ItemRegister]</c> class that
/// surfaces the <c>attributes</c> from its JSON asset(s) as members, so gameplay code reads typed
/// properties instead of stringly-keyed <c>Attributes["x"].AsFloat(..)</c> calls.
/// <para>
/// Blocks/items are singletons (one instance per variant), and one C# class often backs several JSON
/// files (e.g. the molten-canal shapes). The generator finds every block/item-type JSON whose
/// <c>class</c> resolves to the registered class and <b>unions</b> their attributes, then per key:
/// </para>
/// <list type="bullet">
/// <item>identical scalar/string across <b>all</b> matching files, with no <c>attributesByType</c>
/// override → a compile-time <c>public const</c> (no runtime JSON read at all);</item>
/// <item>varies between files, uses <c>attributesByType</c>, or is a JSON object/array → an instance
/// property that reads <c>Attributes</c> for the live variant (a JSON object/array is returned as a
/// <see cref="N:Vintagestory.API.Datastructures"/> <c>JsonObject</c> for the caller to
/// <c>.AsObject&lt;T&gt;()</c>).</item>
/// </list>
/// Keys whose PascalCase name would collide with an existing (or inherited) member are skipped with a
/// comment, so generation never breaks a class.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ExAttributeGenerator : IIncrementalGenerator {
  private const string BlockAttr =
    "ExpandedLib.Registries.Entities.BlockRegisterAttribute";
  private const string ItemAttr =
    "ExpandedLib.Registries.Entities.ItemRegisterAttribute";

  public void Initialize(IncrementalGeneratorInitializationContext context) {
    var blocks = context
      .SyntaxProvider.ForAttributeWithMetadataName(
        BlockAttr,
        predicate: static (n, _) => n is ClassDeclarationSyntax,
        transform: static (ctx, _) => GetClass(ctx)
      )
      .Where(static c => c is not null)
      .Collect();

    var items = context
      .SyntaxProvider.ForAttributeWithMetadataName(
        ItemAttr,
        predicate: static (n, _) => n is ClassDeclarationSyntax,
        transform: static (ctx, _) => GetClass(ctx)
      )
      .Where(static c => c is not null)
      .Collect();

    var jsons = context
      .AdditionalTextsProvider.Where(static t => {
        string p = t.Path.Replace('\\', '/');
        return (p.Contains("/blocktypes/") || p.Contains("/itemtypes/"))
          && p.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
      })
      .Select(static (t, ct) => GetJson(t, ct))
      .Where(static j => j is not null)
      .Collect();

    var combined = blocks.Combine(items).Combine(jsons);
    context.RegisterSourceOutput(
      combined,
      static (spc, data) => {
        var ((blockClasses, itemClasses), jsonModels) = data;
        EmitAll(
          spc,
          blockClasses.Concat(itemClasses).Select(c => c!),
          jsonModels!
        );
      }
    );
  }

  #region Model extraction
  private static ClassModel? GetClass(GeneratorAttributeSyntaxContext ctx) {
    if (ctx.TargetSymbol is not INamedTypeSymbol type)
      return null;

    var attr = ctx.Attributes[0];
    string? code =
      attr.ConstructorArguments.Length > 0
        ? attr.ConstructorArguments[0].Value as string
        : null;

    var members = new HashSet<string>(StringComparer.Ordinal);
    for (INamedTypeSymbol? t = type; t is not null; t = t.BaseType)
      foreach (var m in t.GetMembers())
        members.Add(m.Name);

    // Simple names of the base-type chain, immediate base first - used to inherit/hide members an
    // ancestor [BlockRegister] class already generates (avoids CS0108 in e.g. the canal chain).
    var baseNames = ImmutableArray.CreateBuilder<string>();
    for (INamedTypeSymbol? bt = type.BaseType; bt is not null; bt = bt.BaseType)
      baseNames.Add(bt.Name);

    string? ns = type.ContainingNamespace.IsGlobalNamespace
      ? null
      : type.ContainingNamespace.ToDisplayString();

    return new ClassModel(
      ns,
      type.Name,
      type.DeclaredAccessibility == Accessibility.Public,
      code ?? type.Name,
      new EquatableArray<string>(members.ToImmutableArray()),
      new EquatableArray<string>(baseNames.ToImmutable())
    );
  }

  private static JsonModel? GetJson(AdditionalText text, CancellationToken ct) {
    string? content = text.GetText(ct)?.ToString();
    if (content is null)
      return null;

    var root = MiniJson.Parse(content);
    if (root is null || root.Kind != JKind.Object)
      return null;

    // Only files that name a custom class can map back to a [BlockRegister]/[ItemRegister] type.
    if (!root.TryGet("class", out var cls) || cls.Kind != JKind.String)
      return null;

    string last = LastSegment(cls.Str!);

    var plain = ImmutableArray.CreateBuilder<AttrEntry>();
    if (root.TryGet("attributes", out var attrs) && attrs.Kind == JKind.Object)
      foreach (var kv in attrs.Obj!)
        plain.Add(
          new AttrEntry(kv.Key, kv.Value.Kind, kv.Value.Raw, kv.Value.Str)
        );

    var byType = new HashSet<string>(StringComparer.Ordinal);
    if (
      root.TryGet("attributesByType", out var abt)
      && abt.Kind == JKind.Object
    )
      foreach (var entry in abt.Obj!.Values)
        if (entry.Kind == JKind.Object)
          foreach (var k in entry.Obj!.Keys)
            byType.Add(k);

    return new JsonModel(
      last,
      new EquatableArray<AttrEntry>(plain.ToImmutable()),
      new EquatableArray<string>(byType.ToImmutableArray())
    );
  }

  private static string LastSegment(string s) {
    int dot = s.LastIndexOf('.');
    return dot >= 0 ? s.Substring(dot + 1) : s;
  }
  #endregion

  #region Emission
  private static void EmitAll(
    SourceProductionContext spc,
    IEnumerable<ClassModel> classes,
    ImmutableArray<JsonModel> jsons
  ) {
    // Phase 1: plan each class's members (key -> member source line, no indent / no `new`).
    var plans = new Dictionary<
      string,
      (ClassModel Cls, Dictionary<string, string> Members)
    >(StringComparer.Ordinal);
    foreach (var cls in classes)
      plans[cls.Name] = (cls, PlanMembers(cls, jsons));

    // Phase 2: emit, inheriting an ancestor's identical member (skip) or hiding a differing one
    // (`new`) so a [BlockRegister] subclass never produces a CS0108 hiding warning.
    foreach (var entry in plans.Values) {
      var (cls, members) = entry;
      if (members.Count == 0)
        continue;

      var body = new StringBuilder();
      foreach (var kv in members) {
        string? ancestorLine = NearestAncestorMember(cls, kv.Key, plans);
        if (ancestorLine == kv.Value)
          continue; // inherited unchanged
        string line = ancestorLine is null
          ? kv.Value
          : kv.Value.Replace("public ", "public new ");
        body.Append("  ").AppendLine(line);
      }

      if (body.Length == 0)
        continue;

      string acc = cls.IsPublic ? "public" : "internal";
      var sb = new StringBuilder();
      sb.AppendLine(
        "// <auto-generated/> Block/item attribute accessors. Do not edit."
      );
      sb.AppendLine("#nullable enable");
      sb.AppendLine("using Vintagestory.API.Datastructures;");
      sb.AppendLine();
      if (cls.Namespace is not null) {
        sb.AppendLine($"namespace {cls.Namespace};");
        sb.AppendLine();
      }
      sb.AppendLine($"{acc} partial class {cls.Name}");
      sb.AppendLine("{");
      sb.Append(body);
      sb.AppendLine("}");

      spc.AddSource(
        $"{cls.Name}.Attributes.g.cs",
        SourceText.From(sb.ToString(), Encoding.UTF8)
      );
    }
  }

  /// <summary>The member source for <paramref name="key"/> from the nearest base-chain class that
  /// also generates it, or null if no ancestor does.</summary>
  private static string? NearestAncestorMember(
    ClassModel cls,
    string key,
    Dictionary<
      string,
      (ClassModel Cls, Dictionary<string, string> Members)
    > plans
  ) {
    foreach (string baseName in cls.BaseTypeNames.AsSpan())
      if (
        plans.TryGetValue(baseName, out var anc)
        && anc.Members.TryGetValue(key, out var line)
      )
        return line;
    return null;
  }

  private static Dictionary<string, string> PlanMembers(
    ClassModel cls,
    ImmutableArray<JsonModel> jsons
  ) {
    var result = new Dictionary<string, string>(StringComparer.Ordinal);
    var matching = jsons
      .Where(j => j.ClassLastSegment == cls.BaseKey)
      .ToArray();
    if (matching.Length == 0)
      return result;

    // key -> occurrences in the plain "attributes" of each matching file (absent files omitted).
    var plainByKey = new Dictionary<string, List<AttrEntry>>(
      StringComparer.Ordinal
    );
    var byTypeKeys = new HashSet<string>(StringComparer.Ordinal);
    foreach (var j in matching) {
      foreach (var e in j.PlainAttrs.AsSpan()) {
        if (!plainByKey.TryGetValue(e.Key, out var list))
          plainByKey[e.Key] = list = [];
        list.Add(e);
      }
      foreach (var k in j.ByTypeKeys.AsSpan())
        byTypeKeys.Add(k);
    }

    var allKeys = new SortedSet<string>(StringComparer.Ordinal);
    allKeys.UnionWith(plainByKey.Keys);
    allKeys.UnionWith(byTypeKeys);

    var existing = new HashSet<string>(
      cls.ExistingMembers.AsSpan().ToArray(),
      StringComparer.Ordinal
    );

    foreach (string key in allKeys) {
      string prop = Pascal(key);
      if (existing.Contains(prop))
        continue; // collides with a real (source) member on the class or a base type.

      plainByKey.TryGetValue(key, out var occ);
      bool presentInAll = occ is not null && occ.Count == matching.Length;
      bool inByType = byTypeKeys.Contains(key);
      JKind kind = occ is { Count: > 0 } ? occ[0].Kind : JKind.Object;
      bool isComplex = kind is JKind.Object or JKind.Array;

      bool constant = false;
      if (presentInAll && !inByType && !isComplex) {
        string first = occ![0].Raw;
        constant = occ.All(e => e.Raw == first);
      }

      result[key] = constant
        ? ConstMember(prop, occ![0])
        : InstanceMember(prop, key, kind);
    }

    return result;
  }

  private static string ConstMember(string prop, AttrEntry e) =>
    e.Kind switch {
      JKind.Bool => $"public const bool {prop} = {e.Raw};",
      JKind.String =>
        $"public const string {prop} = {Literal(e.Str ?? string.Empty)};",
      JKind.Number when IsInteger(e.Raw) =>
        $"public const int {prop} = {e.Raw};",
      JKind.Number => $"public const float {prop} = {e.Raw}f;",
      _ => $"// '{prop}' is not a bakeable constant.",
    };

  private static string InstanceMember(string prop, string key, JKind kind) =>
    kind switch {
      JKind.Bool =>
        $"public bool {prop} => Attributes?[\"{key}\"].AsBool(false) ?? false;",
      JKind.String =>
        $"public string? {prop} => Attributes?[\"{key}\"]?.AsString();",
      JKind.Number =>
        $"public float {prop} => Attributes?[\"{key}\"].AsFloat(0f) ?? 0f;",
      // Object / Array (or attributesByType-only): hand back the node for .AsObject<T>().
      _ => $"public JsonObject? {prop} => Attributes?[\"{key}\"];",
    };

  private static bool IsInteger(string raw) =>
    raw.IndexOf('.') < 0 && raw.IndexOf('e') < 0 && raw.IndexOf('E') < 0;

  private static string Pascal(string key) =>
    key.Length == 0 ? key : char.ToUpperInvariant(key[0]) + key.Substring(1);

  private static string Literal(string s) =>
    "\""
    + s.Replace("\\", "\\\\")
      .Replace("\"", "\\\"")
      .Replace("\n", "\\n")
      .Replace("\r", "\\r")
      .Replace("\t", "\\t")
    + "\"";
  #endregion
}

internal sealed record ClassModel(
  string? Namespace,
  string Name,
  bool IsPublic,
  string BaseKey,
  EquatableArray<string> ExistingMembers,
  EquatableArray<string> BaseTypeNames
);

internal sealed record AttrEntry(
  string Key,
  JKind Kind,
  string Raw,
  string? Str
);

internal sealed record JsonModel(
  string ClassLastSegment,
  EquatableArray<AttrEntry> PlainAttrs,
  EquatableArray<string> ByTypeKeys
);

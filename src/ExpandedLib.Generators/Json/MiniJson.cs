using System;
using System.Collections.Generic;

namespace ExpandedLib.Generators.Json;

internal enum JKind {
  Object,
  Array,
  String,
  Number,
  Bool,
  Null,
}

/// <summary>
/// Parsed JSON value. Deliberately tiny - the generator only needs to walk a block/item type's
/// <c>class</c> string and <c>attributes</c> object, compare values across files and classify their
/// kinds; it never round-trips. <see cref="Raw"/> holds the verbatim source text for a value so two
/// values can be compared exactly without re-serialising.
/// </summary>
internal sealed class JVal {
  public JKind Kind;
  public Dictionary<string, JVal>? Obj;
  public List<JVal>? Arr;

  /// <summary>Decoded text for a <see cref="JKind.String"/>.</summary>
  public string? Str;

  /// <summary>Verbatim source slice for any value (used for exact cross-file equality).</summary>
  public string Raw = string.Empty;

  public bool TryGet(string key, out JVal val) {
    if (Kind == JKind.Object && Obj != null && Obj.TryGetValue(key, out var v)) {
      val = v;
      return true;
    }
    val = null!;
    return false;
  }
}

/// <summary>
/// Minimal, allocation-light JSON reader tolerant of the things Vintage Story asset JSON allows that
/// strict parsers reject: <c>//</c> and <c>/* */</c> comments and trailing commas. Hand-rolled so the
/// source generator carries <b>no external dependency</b> (analyzers must bundle their deps, which is
/// fragile for project-reference analyzers). Returns null on malformed input rather than throwing, so
/// one bad asset file can't break the whole build.
/// </summary>
internal static class MiniJson {
  public static JVal? Parse(string text) {
    try {
      int i = 0;
      var v = ParseValue(text, ref i);
      SkipTrivia(text, ref i);
      return v;
    } catch {
      return null;
    }
  }

  private static JVal ParseValue(string s, ref int i) {
    SkipTrivia(s, ref i);
    int start = i;
    char c = s[i];
    switch (c) {
      case '{':
        return Finish(ParseObject(s, ref i), s, start, i);
      case '[':
        return Finish(ParseArray(s, ref i), s, start, i);
      case '"':
        var str = new JVal { Kind = JKind.String, Str = ParseString(s, ref i) };
        return Finish(str, s, start, i);
      default:
        return Finish(ParseLiteral(s, ref i), s, start, i);
    }
  }

  private static JVal Finish(JVal v, string s, int start, int end) {
    v.Raw = s.Substring(start, end - start).Trim();
    return v;
  }

  private static JVal ParseObject(string s, ref int i) {
    var obj = new Dictionary<string, JVal>(StringComparer.Ordinal);
    i++; // consume {
    SkipTrivia(s, ref i);
    if (s[i] == '}') {
      i++;
      return new JVal { Kind = JKind.Object, Obj = obj };
    }
    while (true) {
      SkipTrivia(s, ref i);
      string key = ParseString(s, ref i);
      SkipTrivia(s, ref i);
      i++; // consume :
      obj[key] = ParseValue(s, ref i);
      SkipTrivia(s, ref i);
      char c = s[i++];
      if (c == ',') {
        SkipTrivia(s, ref i);
        if (s[i] == '}') {
          i++;
          break;
        }
        continue;
      }
      if (c == '}')
        break;
    }
    return new JVal { Kind = JKind.Object, Obj = obj };
  }

  private static JVal ParseArray(string s, ref int i) {
    var arr = new List<JVal>();
    i++; // consume [
    SkipTrivia(s, ref i);
    if (s[i] == ']') {
      i++;
      return new JVal { Kind = JKind.Array, Arr = arr };
    }
    while (true) {
      arr.Add(ParseValue(s, ref i));
      SkipTrivia(s, ref i);
      char c = s[i++];
      if (c == ',') {
        SkipTrivia(s, ref i);
        if (s[i] == ']') {
          i++;
          break;
        }
        continue;
      }
      if (c == ']')
        break;
    }
    return new JVal { Kind = JKind.Array, Arr = arr };
  }

  private static string ParseString(string s, ref int i) {
    var sb = new System.Text.StringBuilder();
    i++; // consume opening quote
    while (true) {
      char c = s[i++];
      if (c == '"')
        break;
      if (c == '\\') {
        char e = s[i++];
        sb.Append(
          e switch {
            'n' => '\n',
            't' => '\t',
            'r' => '\r',
            'b' => '\b',
            'f' => '\f',
            '/' => '/',
            '\\' => '\\',
            '"' => '"',
            'u' => (char)Convert.ToInt32(s.Substring(i, 4), 16),
            _ => e,
          }
        );
        if (e == 'u')
          i += 4;
        continue;
      }
      sb.Append(c);
    }
    return sb.ToString();
  }

  private static JVal ParseLiteral(string s, ref int i) {
    int start = i;
    while (i < s.Length && ",}] \t\r\n/".IndexOf(s[i]) < 0)
      i++;
    string tok = s.Substring(start, i - start);
    return tok switch {
      "true" or "false" => new JVal { Kind = JKind.Bool },
      "null" => new JVal { Kind = JKind.Null },
      _ => new JVal { Kind = JKind.Number },
    };
  }

  /// <summary>Skips whitespace and both comment styles.</summary>
  private static void SkipTrivia(string s, ref int i) {
    while (i < s.Length) {
      char c = s[i];
      if (c is ' ' or '\t' or '\r' or '\n') {
        i++;
      } else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/') {
        i += 2;
        while (i < s.Length && s[i] != '\n')
          i++;
      } else if (c == '/' && i + 1 < s.Length && s[i + 1] == '*') {
        i += 2;
        while (i + 1 < s.Length && !(s[i] == '*' && s[i + 1] == '/'))
          i++;
        i += 2;
      } else {
        break;
      }
    }
  }
}

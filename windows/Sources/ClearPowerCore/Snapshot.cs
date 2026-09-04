// The frontend/backend contract: a flat dictionary with the same keys and sentinels as
// the Linux daemon's D-Bus `Snapshot` (see daemon/data/org.clearpower.Daemon1.xml and
// the README). -1 means "unknown / unavailable" and must render as "–", never as zero.
// `bat_w` is positive into the battery.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ClearPower.Core
{
    public static class Snapshot
    {
        public static double? Double(object? v)
        {
            switch (v)
            {
                case double d: return d;
                case float f: return f;
                case int i: return i;
                case long l: return l;
                case decimal m: return (double)m;
                case bool b: return b ? 1 : 0;
                default: return null;
            }
        }

        public static int? Int(object? v)
        {
            switch (v)
            {
                case int i: return i;
                case long l: return (int)l;
                case double d: return double.IsNaN(d) || double.IsInfinity(d) ? (int?)null : (int)d;
                case float f: return (int)f;
                case decimal m: return (int)m;
                case bool b: return b ? 1 : 0;
                default: return null;
            }
        }

        public static bool? Bool(object? v)
        {
            switch (v)
            {
                case bool b: return b;
                case int i: return i != 0;
                case long l: return l != 0;
                default: return null;
            }
        }

        public static string? String(object? v) => v as string;

        /// <summary>JSON text for --once and debugging; keys sorted for stable diffs.</summary>
        public static string Json(IDictionary<string, object?> snap, bool pretty = true)
        {
            var clean = new SortedDictionary<string, object?>(StringComparer.Ordinal);
            foreach (var kv in snap)
            {
                var v = kv.Value;
                if (v is double d && (double.IsNaN(d) || double.IsInfinity(d))) v = -1.0;
                clean[kv.Key] = v;
            }
            return Core.Json.Serialize(clean, pretty);
        }
    }

    /// <summary>Typed accessors mirroring the Swift/JS helpers (d/i/b/s).</summary>
    public static class SnapshotExtensions
    {
        public static double D(this IDictionary<string, object?> d, string key, double fallback = -1)
            => d.TryGetValue(key, out var v) ? (Snapshot.Double(v) ?? fallback) : fallback;

        public static int I(this IDictionary<string, object?> d, string key, int fallback = -1)
            => d.TryGetValue(key, out var v) ? (Snapshot.Int(v) ?? fallback) : fallback;

        public static bool B(this IDictionary<string, object?> d, string key, bool fallback = false)
            => d.TryGetValue(key, out var v) ? (Snapshot.Bool(v) ?? fallback) : fallback;

        public static string S(this IDictionary<string, object?> d, string key, string fallback = "")
            => d.TryGetValue(key, out var v) ? (Snapshot.String(v) ?? fallback) : fallback;

        /// <summary>Merge `other` into `d`, overwriting existing keys (Swift `merge { $1 }`).</summary>
        public static void MergeFrom(this IDictionary<string, object?> d, IDictionary<string, object?> other)
        {
            foreach (var kv in other) d[kv.Key] = kv.Value;
        }
    }

    /// <summary>
    /// Minimal JSON layer on the in-box JavaScriptSerializer (no NuGet): a hand-written
    /// writer (invariant culture, optional pretty print) and a parser that returns
    /// Dictionary / List / double / string / bool / null.
    /// </summary>
    public static class Json
    {
        public static string Serialize(object? value, bool pretty = false)
        {
            var sb = new StringBuilder();
            Write(sb, value, pretty ? 0 : -1);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int level)
        {
            if (level < 0) return;
            sb.Append('\n');
            for (int i = 0; i < level; i++) sb.Append(' ');
        }

        private static void Write(StringBuilder sb, object? v, int level)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case float f: WriteDouble(sb, f); break;
                case double d: WriteDouble(sb, d); break;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); break;
                case IDictionary<string, object?> dict: WriteDict(sb, dict, level); break;
                case System.Collections.IEnumerable list: WriteList(sb, list, level); break;
                default: WriteString(sb, v.ToString() ?? ""); break;
            }
        }

        private static void WriteDouble(StringBuilder sb, double d)
        {
            if (double.IsNaN(d) || double.IsInfinity(d)) d = -1;
            var s = d.ToString("R", CultureInfo.InvariantCulture);
            sb.Append(s);
            if (s.IndexOfAny(new[] { '.', 'e', 'E' }) < 0) sb.Append(".0");
        }

        private static void WriteDict(StringBuilder sb, IDictionary<string, object?> dict, int level)
        {
            sb.Append('{');
            bool first = true;
            foreach (var kv in dict)
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, level < 0 ? -1 : level + 1);
                WriteString(sb, kv.Key);
                sb.Append(level < 0 ? ":" : ": ");
                Write(sb, kv.Value, level < 0 ? -1 : level + 1);
            }
            if (!first) Indent(sb, level);
            sb.Append('}');
        }

        private static void WriteList(StringBuilder sb, System.Collections.IEnumerable list, int level)
        {
            sb.Append('[');
            bool first = true;
            foreach (var item in list)
            {
                if (!first) sb.Append(',');
                first = false;
                Indent(sb, level < 0 ? -1 : level + 1);
                Write(sb, item, level < 0 ? -1 : level + 1);
            }
            if (!first) Indent(sb, level);
            sb.Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        /// <summary>Parse JSON into Dictionary&lt;string, object?&gt; / List&lt;object?&gt; / double / string / bool / null.</summary>
        public static object? Parse(string text)
        {
            var p = new Parser(text);
            var v = p.Value();
            p.SkipWs();
            if (!p.End) throw new FormatException($"trailing characters at {p.Pos}");
            return v;
        }

        public static Dictionary<string, object?> ParseObject(string text)
            => Parse(text) as Dictionary<string, object?> ?? new Dictionary<string, object?>();

        private sealed class Parser
        {
            private readonly string _s;
            public int Pos;
            public Parser(string s) { _s = s; }
            public bool End => Pos >= _s.Length;

            public void SkipWs()
            {
                while (Pos < _s.Length && char.IsWhiteSpace(_s[Pos])) Pos++;
            }

            private char Peek() => Pos < _s.Length ? _s[Pos] : '\0';

            private void Expect(char c)
            {
                if (Peek() != c) throw new FormatException($"expected '{c}' at {Pos}");
                Pos++;
            }

            public object? Value()
            {
                SkipWs();
                var c = Peek();
                switch (c)
                {
                    case '{': return Object();
                    case '[': return Array();
                    case '"': return String();
                    case 't': Literal("true"); return true;
                    case 'f': Literal("false"); return false;
                    case 'n': Literal("null"); return null;
                    default: return Number();
                }
            }

            private void Literal(string word)
            {
                if (string.CompareOrdinal(_s, Pos, word, 0, word.Length) != 0) throw new FormatException($"bad literal at {Pos}");
                Pos += word.Length;
            }

            private Dictionary<string, object?> Object()
            {
                var o = new Dictionary<string, object?>(StringComparer.Ordinal);
                Expect('{');
                SkipWs();
                if (Peek() == '}') { Pos++; return o; }
                while (true)
                {
                    SkipWs();
                    var key = String();
                    SkipWs();
                    Expect(':');
                    o[key] = Value();
                    SkipWs();
                    if (Peek() == ',') { Pos++; continue; }
                    Expect('}');
                    return o;
                }
            }

            private List<object?> Array()
            {
                var a = new List<object?>();
                Expect('[');
                SkipWs();
                if (Peek() == ']') { Pos++; return a; }
                while (true)
                {
                    a.Add(Value());
                    SkipWs();
                    if (Peek() == ',') { Pos++; continue; }
                    Expect(']');
                    return a;
                }
            }

            private string String()
            {
                Expect('"');
                var sb = new StringBuilder();
                while (true)
                {
                    if (End) throw new FormatException("unterminated string");
                    var c = _s[Pos++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    var e = _s[Pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            sb.Append((char)Convert.ToInt32(_s.Substring(Pos, 4), 16));
                            Pos += 4;
                            break;
                        default: throw new FormatException($"bad escape at {Pos}");
                    }
                }
            }

            private double Number()
            {
                var start = Pos;
                while (!End && "+-0123456789.eE".IndexOf(_s[Pos]) >= 0) Pos++;
                if (start == Pos) throw new FormatException($"unexpected character at {Pos}");
                return double.Parse(_s.Substring(start, Pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}

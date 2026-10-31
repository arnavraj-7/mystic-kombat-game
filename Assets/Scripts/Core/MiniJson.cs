using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MysticKombat.Core
{
    /// <summary>
    /// Tiny JSON reader/writer. Objects become Dictionary&lt;string, object&gt;, arrays List&lt;object&gt;,
    /// numbers double, plus string/bool/null. Used by both Unity and the headless dotnet harness,
    /// so the grimoire (fighters.json) loads the same way everywhere.
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string json)
        {
            var p = new Parser(json);
            object v = p.ParseValue();
            p.SkipWs();
            if (!p.End) throw new FormatException("Trailing characters at " + p.Pos);
            return v;
        }

        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value);
            return sb.ToString();
        }

        static void Write(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: WriteString(sb, s); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case IDictionary<string, object> d:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in d)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        WriteString(sb, kv.Key);
                        sb.Append(':');
                        Write(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                case System.Collections.IEnumerable list:
                    sb.Append('[');
                    bool f = true;
                    foreach (var item in list)
                    {
                        if (!f) sb.Append(',');
                        f = false;
                        Write(sb, item);
                    }
                    sb.Append(']');
                    break;
                case float fl: sb.Append(fl.ToString("R", CultureInfo.InvariantCulture)); break;
                case double db: sb.Append(db.ToString("R", CultureInfo.InvariantCulture)); break;
                case IFormattable n: sb.Append(n.ToString(null, CultureInfo.InvariantCulture)); break;
                default: WriteString(sb, v.ToString()); break;
            }
        }

        static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
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

        sealed class Parser
        {
            readonly string s;
            int i;
            public Parser(string s) { this.s = s; }
            public int Pos => i;
            public bool End => i >= s.Length;

            public void SkipWs()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (c == ' ' || c == '\t' || c == '\n' || c == '\r') { i++; continue; }
                    // allow // line comments so designers can annotate the grimoire
                    if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                    {
                        while (i < s.Length && s[i] != '\n') i++;
                        continue;
                    }
                    break;
                }
            }

            public object ParseValue()
            {
                SkipWs();
                if (End) throw new FormatException("Unexpected end of JSON");
                char c = s[i];
                if (c == '{') return ParseObject();
                if (c == '[') return ParseArray();
                if (c == '"') return ParseString();
                if (c == 't' && s.Substring(i).StartsWith("true")) { i += 4; return true; }
                if (c == 'f' && s.Substring(i).StartsWith("false")) { i += 5; return false; }
                if (c == 'n' && s.Substring(i).StartsWith("null")) { i += 4; return null; }
                return ParseNumber();
            }

            Dictionary<string, object> ParseObject()
            {
                var d = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (s[i] == '}') { i++; return d; }
                while (true)
                {
                    SkipWs();
                    string key = ParseString();
                    SkipWs();
                    if (s[i] != ':') throw new FormatException("Expected ':' at " + i);
                    i++;
                    d[key] = ParseValue();
                    SkipWs();
                    if (s[i] == ',') { i++; SkipWs(); if (s[i] == '}') { i++; return d; } continue; }
                    if (s[i] == '}') { i++; return d; }
                    throw new FormatException("Expected ',' or '}' at " + i);
                }
            }

            List<object> ParseArray()
            {
                var l = new List<object>();
                i++;
                SkipWs();
                if (s[i] == ']') { i++; return l; }
                while (true)
                {
                    l.Add(ParseValue());
                    SkipWs();
                    if (s[i] == ',') { i++; SkipWs(); if (s[i] == ']') { i++; return l; } continue; }
                    if (s[i] == ']') { i++; return l; }
                    throw new FormatException("Expected ',' or ']' at " + i);
                }
            }

            string ParseString()
            {
                if (s[i] != '"') throw new FormatException("Expected string at " + i);
                i++;
                var sb = new StringBuilder();
                while (true)
                {
                    char c = s[i++];
                    if (c == '"') break;
                    if (c == '\\')
                    {
                        char e = s[i++];
                        switch (e)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 't': sb.Append('\t'); break;
                            case 'r': sb.Append('\r'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case 'u': sb.Append((char)Convert.ToInt32(s.Substring(i, 4), 16)); i += 4; break;
                            default: sb.Append(e); break;
                        }
                    }
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            double ParseNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (start == i) throw new FormatException("Unexpected character '" + s[i] + "' at " + i);
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }

    /// <summary>Typed accessors over MiniJson dictionaries.</summary>
    public static class J
    {
        public static Dictionary<string, object> Obj(object o, string key)
            => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

        public static List<object> Arr(object o, string key)
            => o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as List<object> : null;

        public static bool Has(Dictionary<string, object> d, string key) => d != null && d.ContainsKey(key);

        public static string Str(Dictionary<string, object> d, string key, string def = null)
            => d != null && d.TryGetValue(key, out var v) && v is string s ? s : def;

        public static float F(Dictionary<string, object> d, string key, float def = 0f)
            => d != null && d.TryGetValue(key, out var v) && v is double n ? (float)n : def;

        public static int I(Dictionary<string, object> d, string key, int def = 0)
            => d != null && d.TryGetValue(key, out var v) && v is double n ? (int)Math.Round(n) : def;

        public static bool B(Dictionary<string, object> d, string key, bool def = false)
            => d != null && d.TryGetValue(key, out var v) && v is bool b ? b : def;

        public static float[] Floats(Dictionary<string, object> d, string key)
        {
            if (d == null || !d.TryGetValue(key, out var v) || !(v is List<object> l)) return null;
            var r = new float[l.Count];
            for (int k = 0; k < l.Count; k++) r[k] = l[k] is double n ? (float)n : 0f;
            return r;
        }
    }
}

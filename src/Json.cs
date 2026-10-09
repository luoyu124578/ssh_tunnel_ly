using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SshTunnelLy
{
    /// <summary>极简 JSON 读写（无第三方依赖，避免引入额外程序集）。</summary>
    public sealed class JsonMap : Dictionary<string, object>
    {
        public JsonMap() : base(StringComparer.Ordinal) { }
    }

    public sealed class JsonList : List<object> { }

    public static class Json
    {
        // ---------------- 写 ----------------

        public static string Write(object value, bool pretty)
        {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, value, pretty, 0);
            return sb.ToString();
        }

        private static void Indent(StringBuilder sb, int depth)
        {
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }

        private static void WriteValue(StringBuilder sb, object value, bool pretty, int depth)
        {
            if (value == null) { sb.Append("null"); return; }
            if (value is string) { WriteString(sb, (string)value); return; }
            if (value is bool) { sb.Append(((bool)value) ? "true" : "false"); return; }
            if (value is int || value is long || value is short || value is byte)
            {
                sb.Append(Convert.ToInt64(value).ToString(CultureInfo.InvariantCulture));
                return;
            }
            if (value is float || value is double || value is decimal)
            {
                double d = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15) sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            IDictionary<string, object> map = value as IDictionary<string, object>;
            if (map != null)
            {
                if (map.Count == 0) { sb.Append("{}"); return; }
                sb.Append('{');
                bool first = true;
                foreach (KeyValuePair<string, object> kv in map)
                {
                    if (!first) sb.Append(',');
                    first = false;
                    if (pretty) Indent(sb, depth + 1);
                    WriteString(sb, kv.Key);
                    sb.Append(':');
                    if (pretty) sb.Append(' ');
                    WriteValue(sb, kv.Value, pretty, depth + 1);
                }
                if (pretty) Indent(sb, depth);
                sb.Append('}');
                return;
            }

            IEnumerable seq = value as IEnumerable;
            if (seq != null)
            {
                List<object> items = new List<object>();
                foreach (object o in seq) items.Add(o);
                if (items.Count == 0) { sb.Append("[]"); return; }
                sb.Append('[');
                for (int i = 0; i < items.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    if (pretty) Indent(sb, depth + 1);
                    WriteValue(sb, items[i], pretty, depth + 1);
                }
                if (pretty) Indent(sb, depth);
                sb.Append(']');
                return;
            }

            WriteString(sb, value.ToString());
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }

        // ---------------- 读 ----------------

        public static object Parse(string text)
        {
            int i = 0;
            object v = ParseValue(text, ref i);
            return v;
        }

        private static void SkipWs(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\r' || s[i] == '\n')) i++;
        }

        private static object ParseValue(string s, ref int i)
        {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("JSON 意外结束");
            char c = s[i];
            if (c == '{') return ParseObject(s, ref i);
            if (c == '[') return ParseArray(s, ref i);
            if (c == '"') return ParseString(s, ref i);
            if (c == 't') { Expect(s, ref i, "true"); return true; }
            if (c == 'f') { Expect(s, ref i, "false"); return false; }
            if (c == 'n') { Expect(s, ref i, "null"); return null; }
            return ParseNumber(s, ref i);
        }

        private static void Expect(string s, ref int i, string word)
        {
            if (i + word.Length > s.Length || string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("JSON 非法字面量，位置 " + i);
            i += word.Length;
        }

        private static JsonMap ParseObject(string s, ref int i)
        {
            JsonMap map = new JsonMap();
            i++; // {
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return map; }
            while (true)
            {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"') throw new FormatException("JSON 期待键名，位置 " + i);
                string key = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':') throw new FormatException("JSON 期待 ':'，位置 " + i);
                i++;
                map[key] = ParseValue(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 对象未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; break; }
                throw new FormatException("JSON 期待 ',' 或 '}'，位置 " + i);
            }
            return map;
        }

        private static JsonList ParseArray(string s, ref int i)
        {
            JsonList list = new JsonList();
            i++; // [
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return list; }
            while (true)
            {
                list.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i >= s.Length) throw new FormatException("JSON 数组未闭合");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; break; }
                throw new FormatException("JSON 期待 ',' 或 ']'，位置 " + i);
            }
            return list;
        }

        private static string ParseString(string s, ref int i)
        {
            StringBuilder sb = new StringBuilder();
            i++; // opening quote
            while (true)
            {
                if (i >= s.Length) throw new FormatException("JSON 字符串未闭合");
                char c = s[i++];
                if (c == '"') break;
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("JSON 转义未完成");
                char e = s[i++];
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
                        if (i + 4 > s.Length) throw new FormatException("JSON \\u 转义不完整");
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default: throw new FormatException("JSON 非法转义 \\" + e);
                }
            }
            return sb.ToString();
        }

        private static object ParseNumber(string s, ref int i)
        {
            int start = i;
            while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '-' || s[i] == '+' || s[i] == '.' || s[i] == 'e' || s[i] == 'E')) i++;
            string t = s.Substring(start, i - start);
            if (t.Length == 0) throw new FormatException("JSON 非法数值，位置 " + start);
            long l;
            if (t.IndexOf('.') < 0 && t.IndexOf('e') < 0 && t.IndexOf('E') < 0 && long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
                return l;
            double d;
            if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d)) return d;
            throw new FormatException("JSON 非法数值: " + t);
        }

        // ---------------- 取值助手 ----------------

        public static string Str(IDictionary<string, object> map, string key, string def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && v != null) return Convert.ToString(v, CultureInfo.InvariantCulture);
            return def;
        }

        public static int Int(IDictionary<string, object> map, string key, int def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && v != null)
            {
                long l;
                if (v is long) return (int)(long)v;
                if (v is double) return (int)(double)v;
                if (long.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), out l)) return (int)l;
            }
            return def;
        }

        public static bool Bool(IDictionary<string, object> map, string key, bool def)
        {
            object v;
            if (map != null && map.TryGetValue(key, out v) && v != null)
            {
                if (v is bool) return (bool)v;
                string t = Convert.ToString(v, CultureInfo.InvariantCulture);
                if (string.Equals(t, "true", StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(t, "false", StringComparison.OrdinalIgnoreCase)) return false;
            }
            return def;
        }
    }
}

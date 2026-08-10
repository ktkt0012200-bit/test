using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace SkullDive.Net
{
    /// <summary>
    /// 最小限の JSON 書き出し。
    ///
    /// 外部ライブラリを使わない理由:
    ///  - Unity に JSON パッケージを追加させないため(セットアップの手間とバージョン依存を無くす)
    ///  - リフレクションを使わないため。IL2CPP のマネージドコード除去は、
    ///    リフレクション経由でしか触られないフィールドを削ることがあり、
    ///    「エディタでは動くが実機で空になる」という形で壊れる
    ///  - サーバとクライアントで同一の実装を使えるので、書式のズレが原理的に起きない
    /// </summary>
    public sealed class JsonWriter
    {
        private readonly StringBuilder _builder = new StringBuilder(512);
        private bool _needComma;

        public JsonWriter BeginObject()
        {
            Separate();
            _builder.Append('{');
            _needComma = false;
            return this;
        }

        public JsonWriter EndObject()
        {
            _builder.Append('}');
            _needComma = true;
            return this;
        }

        public JsonWriter BeginArray()
        {
            Separate();
            _builder.Append('[');
            _needComma = false;
            return this;
        }

        public JsonWriter EndArray()
        {
            _builder.Append(']');
            _needComma = true;
            return this;
        }

        /// <summary>キーを書く。直後に値を 1 つ書くこと。</summary>
        public JsonWriter Name(string name)
        {
            Separate();
            WriteString(name);
            _builder.Append(':');
            _needComma = false;
            return this;
        }

        public JsonWriter Value(int value)
        {
            Separate();
            _builder.Append(value.ToString(CultureInfo.InvariantCulture));
            _needComma = true;
            return this;
        }

        public JsonWriter Value(bool value)
        {
            Separate();
            _builder.Append(value ? "true" : "false");
            _needComma = true;
            return this;
        }

        public JsonWriter Value(string value)
        {
            Separate();
            if (value == null) _builder.Append("null");
            else WriteString(value);
            _needComma = true;
            return this;
        }

        public JsonWriter Field(string name, int value)
        {
            return Name(name).Value(value);
        }

        public JsonWriter Field(string name, bool value)
        {
            return Name(name).Value(value);
        }

        public JsonWriter Field(string name, string value)
        {
            return Name(name).Value(value);
        }

        public JsonWriter IntArray(string name, int[] values)
        {
            Name(name).BeginArray();
            if (values != null)
            {
                for (int i = 0; i < values.Length; i++) Value(values[i]);
            }
            return EndArray();
        }

        public JsonWriter Null()
        {
            Separate();
            _builder.Append("null");
            _needComma = true;
            return this;
        }

        private void Separate()
        {
            if (_needComma) _builder.Append(',');
        }

        private void WriteString(string value)
        {
            _builder.Append('"');
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '"': _builder.Append("\\\""); break;
                    case '\\': _builder.Append("\\\\"); break;
                    case '\n': _builder.Append("\\n"); break;
                    case '\r': _builder.Append("\\r"); break;
                    case '\t': _builder.Append("\\t"); break;
                    case '\b': _builder.Append("\\b"); break;
                    case '\f': _builder.Append("\\f"); break;
                    default:
                        if (c < 0x20)
                        {
                            _builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            // 日本語などはそのまま出す(UTF-8 で送るのでエスケープ不要)。
                            _builder.Append(c);
                        }
                        break;
                }
            }
            _builder.Append('"');
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }

    /// <summary>
    /// 最小限の JSON 読み取り。DTO は数値・真偽値・文字列・配列・オブジェクトだけで
    /// 構成されているので、それらに限って扱う。知らないキーは黙って無視する
    /// (前方互換のため。サーバが新しいフィールドを足しても古いクライアントが壊れない)。
    /// </summary>
    public sealed class JsonValue
    {
        public enum Kind
        {
            Null,
            Bool,
            Number,
            String,
            Array,
            Object,
        }

        public Kind Type { get; private set; }

        private bool _bool;
        private double _number;
        private string _string;
        private List<JsonValue> _array;
        private Dictionary<string, JsonValue> _object;

        public static readonly JsonValue Nothing = new JsonValue { Type = Kind.Null };

        // ------------------------------------------------------------ accessors

        public bool IsNull
        {
            get { return Type == Kind.Null; }
        }

        public int Count
        {
            get { return _array != null ? _array.Count : 0; }
        }

        public JsonValue this[int index]
        {
            get { return _array != null && index >= 0 && index < _array.Count ? _array[index] : Nothing; }
        }

        public JsonValue this[string name]
        {
            get
            {
                JsonValue value;
                if (_object != null && name != null && _object.TryGetValue(name, out value)) return value;
                return Nothing;
            }
        }

        public int AsInt(int fallback = 0)
        {
            if (Type != Kind.Number) return fallback;
            return (int)Math.Round(_number);
        }

        public bool AsBool(bool fallback = false)
        {
            if (Type == Kind.Bool) return _bool;
            if (Type == Kind.Number) return Math.Abs(_number) > double.Epsilon;
            return fallback;
        }

        public string AsString(string fallback = null)
        {
            return Type == Kind.String ? _string : fallback;
        }

        public int[] AsIntArray()
        {
            if (Type != Kind.Array) return new int[0];
            var result = new int[_array.Count];
            for (int i = 0; i < _array.Count; i++) result[i] = _array[i].AsInt();
            return result;
        }

        // ------------------------------------------------------------ parsing

        public static JsonValue Parse(string json)
        {
            if (json == null) throw new ArgumentNullException("json");
            int index = 0;
            var value = ParseValue(json, ref index);
            SkipWhitespace(json, ref index);
            return value;
        }

        private static JsonValue ParseValue(string json, ref int index)
        {
            SkipWhitespace(json, ref index);
            if (index >= json.Length) throw new FormatException("unexpected end of JSON");

            char c = json[index];
            switch (c)
            {
                case '{': return ParseObject(json, ref index);
                case '[': return ParseArray(json, ref index);
                case '"': return new JsonValue { Type = Kind.String, _string = ParseString(json, ref index) };
                case 't':
                    Expect(json, ref index, "true");
                    return new JsonValue { Type = Kind.Bool, _bool = true };
                case 'f':
                    Expect(json, ref index, "false");
                    return new JsonValue { Type = Kind.Bool, _bool = false };
                case 'n':
                    Expect(json, ref index, "null");
                    return Nothing;
                default:
                    return new JsonValue { Type = Kind.Number, _number = ParseNumber(json, ref index) };
            }
        }

        private static JsonValue ParseObject(string json, ref int index)
        {
            var result = new JsonValue { Type = Kind.Object, _object = new Dictionary<string, JsonValue>() };
            index++; // '{'

            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == '}') { index++; return result; }

            while (true)
            {
                SkipWhitespace(json, ref index);
                string name = ParseString(json, ref index);
                SkipWhitespace(json, ref index);
                if (index >= json.Length || json[index] != ':') throw new FormatException("expected ':'");
                index++;

                result._object[name] = ParseValue(json, ref index);

                SkipWhitespace(json, ref index);
                if (index >= json.Length) throw new FormatException("unterminated object");
                if (json[index] == ',') { index++; continue; }
                if (json[index] == '}') { index++; return result; }
                throw new FormatException("expected ',' or '}' at " + index);
            }
        }

        private static JsonValue ParseArray(string json, ref int index)
        {
            var result = new JsonValue { Type = Kind.Array, _array = new List<JsonValue>() };
            index++; // '['

            SkipWhitespace(json, ref index);
            if (index < json.Length && json[index] == ']') { index++; return result; }

            while (true)
            {
                result._array.Add(ParseValue(json, ref index));

                SkipWhitespace(json, ref index);
                if (index >= json.Length) throw new FormatException("unterminated array");
                if (json[index] == ',') { index++; continue; }
                if (json[index] == ']') { index++; return result; }
                throw new FormatException("expected ',' or ']' at " + index);
            }
        }

        private static string ParseString(string json, ref int index)
        {
            if (index >= json.Length || json[index] != '"') throw new FormatException("expected string at " + index);
            index++;

            var builder = new StringBuilder();
            while (index < json.Length)
            {
                char c = json[index++];
                if (c == '"') return builder.ToString();

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (index >= json.Length) break;
                char escape = json[index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'u':
                        if (index + 4 > json.Length) throw new FormatException("bad \\u escape");
                        builder.Append((char)int.Parse(json.Substring(index, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture));
                        index += 4;
                        break;
                    default:
                        throw new FormatException("unknown escape \\" + escape);
                }
            }
            throw new FormatException("unterminated string");
        }

        private static double ParseNumber(string json, ref int index)
        {
            int start = index;
            if (index < json.Length && (json[index] == '-' || json[index] == '+')) index++;
            while (index < json.Length)
            {
                char c = json[index];
                if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') index++;
                else break;
            }
            if (index == start) throw new FormatException("expected number at " + start);
            return double.Parse(json.Substring(start, index - start), NumberStyles.Float,
                CultureInfo.InvariantCulture);
        }

        private static void Expect(string json, ref int index, string literal)
        {
            if (index + literal.Length > json.Length
                || string.CompareOrdinal(json, index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("expected '" + literal + "' at " + index);
            }
            index += literal.Length;
        }

        private static void SkipWhitespace(string json, ref int index)
        {
            while (index < json.Length)
            {
                char c = json[index];
                if (c == ' ' || c == '\t' || c == '\n' || c == '\r') index++;
                else break;
            }
        }
    }
}

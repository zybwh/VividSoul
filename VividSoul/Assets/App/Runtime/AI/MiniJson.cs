#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace VividSoul.Runtime.AI
{
    public static class MiniJson
    {
        public static object? Deserialize(string json)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            var parser = new Parser(json);
            return parser.ParseValue();
        }

        public static string Serialize(object? value)
        {
            var builder = new StringBuilder(256);
            Serializer.SerializeValue(value, builder);
            return builder.ToString();
        }

        private sealed class Parser
        {
            private readonly string json;
            private int index;

            public Parser(string json)
            {
                this.json = json ?? throw new ArgumentNullException(nameof(json));
            }

            public object? ParseValue()
            {
                SkipWhitespace();
                if (index >= json.Length)
                {
                    return null;
                }

                return json[index] switch
                {
                    '{' => ParseObject(),
                    '[' => ParseArray(),
                    '"' => ParseString(),
                    't' => ParseLiteral("true", true),
                    'f' => ParseLiteral("false", false),
                    'n' => ParseLiteral("null", null),
                    _ => ParseNumber(),
                };
            }

            private Dictionary<string, object?> ParseObject()
            {
                var result = new Dictionary<string, object?>(StringComparer.Ordinal);
                index++;

                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    var key = ParseString();
                    SkipWhitespace();
                    Expect(':');
                    var value = ParseValue();
                    result[key] = value;
                    SkipWhitespace();
                    if (TryConsume('}'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private List<object?> ParseArray()
            {
                var result = new List<object?>();
                index++;

                while (true)
                {
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    result.Add(ParseValue());
                    SkipWhitespace();
                    if (TryConsume(']'))
                    {
                        return result;
                    }

                    Expect(',');
                }
            }

            private string ParseString()
            {
                Expect('"');
                var builder = new StringBuilder();
                while (index < json.Length)
                {
                    var character = json[index++];
                    if (character == '"')
                    {
                        return builder.ToString();
                    }

                    if (character != '\\')
                    {
                        builder.Append(character);
                        continue;
                    }

                    if (index >= json.Length)
                    {
                        break;
                    }

                    var escaped = json[index++];
                    switch (escaped)
                    {
                        case '"':
                        case '\\':
                        case '/':
                            builder.Append(escaped);
                            break;
                        case 'b':
                            builder.Append('\b');
                            break;
                        case 'f':
                            builder.Append('\f');
                            break;
                        case 'n':
                            builder.Append('\n');
                            break;
                        case 'r':
                            builder.Append('\r');
                            break;
                        case 't':
                            builder.Append('\t');
                            break;
                        case 'u':
                            if (index + 4 > json.Length)
                            {
                                throw new FormatException("Invalid unicode escape in JSON string.");
                            }

                            var hex = json.Substring(index, 4);
                            builder.Append((char)Convert.ToInt32(hex, 16));
                            index += 4;
                            break;
                        default:
                            throw new FormatException($"Unsupported JSON escape sequence: \\{escaped}");
                    }
                }

                throw new FormatException("Unterminated JSON string.");
            }

            private object? ParseNumber()
            {
                var startIndex = index;
                while (index < json.Length)
                {
                    var character = json[index];
                    if ((character >= '0' && character <= '9')
                        || character == '-'
                        || character == '+'
                        || character == '.'
                        || character == 'e'
                        || character == 'E')
                    {
                        index++;
                        continue;
                    }

                    break;
                }

                var token = json.Substring(startIndex, index - startIndex);
                if (token.IndexOf('.') >= 0 || token.IndexOf('e') >= 0 || token.IndexOf('E') >= 0)
                {
                    return double.Parse(token, CultureInfo.InvariantCulture);
                }

                return long.Parse(token, CultureInfo.InvariantCulture);
            }

            private object? ParseLiteral(string literal, object? value)
            {
                if (index + literal.Length > json.Length
                    || !string.Equals(json.Substring(index, literal.Length), literal, StringComparison.Ordinal))
                {
                    throw new FormatException($"Invalid JSON literal near index {index}.");
                }

                index += literal.Length;
                return value;
            }

            private void SkipWhitespace()
            {
                while (index < json.Length && char.IsWhiteSpace(json[index]))
                {
                    index++;
                }
            }

            private bool TryConsume(char character)
            {
                if (index < json.Length && json[index] == character)
                {
                    index++;
                    return true;
                }

                return false;
            }

            private void Expect(char character)
            {
                SkipWhitespace();
                if (!TryConsume(character))
                {
                    throw new FormatException($"Expected '{character}' near index {index}.");
                }
            }
        }

        private static class Serializer
        {
            public static void SerializeValue(object? value, StringBuilder builder)
            {
                switch (value)
                {
                    case null:
                        builder.Append("null");
                        return;
                    case string stringValue:
                        SerializeString(stringValue, builder);
                        return;
                    case bool boolValue:
                        builder.Append(boolValue ? "true" : "false");
                        return;
                    case IDictionary dictionary:
                        SerializeDictionary(dictionary, builder);
                        return;
                    case IEnumerable enumerable when value is not string:
                        SerializeArray(enumerable, builder);
                        return;
                    case char charValue:
                        SerializeString(charValue.ToString(), builder);
                        return;
                }

                if (value is sbyte or byte or short or ushort or int or uint or long or ulong or float or double or decimal)
                {
                    builder.Append(Convert.ToString(value, CultureInfo.InvariantCulture));
                    return;
                }

                SerializeString(value.ToString() ?? string.Empty, builder);
            }

            private static void SerializeDictionary(IDictionary dictionary, StringBuilder builder)
            {
                builder.Append('{');
                var isFirst = true;
                foreach (DictionaryEntry entry in dictionary)
                {
                    if (!isFirst)
                    {
                        builder.Append(',');
                    }

                    SerializeString(entry.Key.ToString() ?? string.Empty, builder);
                    builder.Append(':');
                    SerializeValue(entry.Value, builder);
                    isFirst = false;
                }

                builder.Append('}');
            }

            private static void SerializeArray(IEnumerable array, StringBuilder builder)
            {
                builder.Append('[');
                var isFirst = true;
                foreach (var item in array)
                {
                    if (!isFirst)
                    {
                        builder.Append(',');
                    }

                    SerializeValue(item, builder);
                    isFirst = false;
                }

                builder.Append(']');
            }

            private static void SerializeString(string value, StringBuilder builder)
            {
                builder.Append('"');
                foreach (var character in value)
                {
                    switch (character)
                    {
                        case '"':
                            builder.Append("\\\"");
                            break;
                        case '\\':
                            builder.Append("\\\\");
                            break;
                        case '\b':
                            builder.Append("\\b");
                            break;
                        case '\f':
                            builder.Append("\\f");
                            break;
                        case '\n':
                            builder.Append("\\n");
                            break;
                        case '\r':
                            builder.Append("\\r");
                            break;
                        case '\t':
                            builder.Append("\\t");
                            break;
                        default:
                            if (character < 32)
                            {
                                builder.Append("\\u");
                                builder.Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                            }
                            else
                            {
                                builder.Append(character);
                            }

                            break;
                    }
                }

                builder.Append('"');
            }
        }
    }
}

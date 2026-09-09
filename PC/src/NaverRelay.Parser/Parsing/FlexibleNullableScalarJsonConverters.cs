using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaverRelay.Parsing
{
    internal sealed class FlexibleNullableInt32JsonConverter : JsonConverter<int?>
    {
        public override bool HandleNull => true;

        public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var numeric))
            {
                return numeric;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(text) || text == "-")
                {
                    return null;
                }

                if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            throw new JsonException($"Cannot convert JSON token {reader.TokenType} to nullable Int32.");
        }

        public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    internal sealed class FlexibleNullableDoubleJsonConverter : JsonConverter<double?>
    {
        public override bool HandleNull => true;

        public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var numeric))
            {
                return numeric;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(text) || text == "-")
                {
                    return null;
                }

                if (double.TryParse(text, NumberStyles.Float | NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out var parsed))
                {
                    return parsed;
                }
            }

            throw new JsonException($"Cannot convert JSON token {reader.TokenType} to nullable Double.");
        }

        public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteNumberValue(value.Value);
            else writer.WriteNullValue();
        }
    }

    internal sealed class FlexibleNullableBooleanJsonConverter : JsonConverter<bool?>
    {
        public override bool HandleNull => true;

        public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return null;
            }

            if (reader.TokenType == JsonTokenType.True) return true;
            if (reader.TokenType == JsonTokenType.False) return false;

            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                return number switch
                {
                    1 => true,
                    0 => false,
                    _ => throw new JsonException($"Numeric boolean must be 0 or 1, but was {number}."),
                };
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString()?.Trim();
                if (string.IsNullOrEmpty(text) || text == "-") return null;
                if (bool.TryParse(text, out var boolean)) return boolean;
                if (text == "1") return true;
                if (text == "0") return false;
            }

            throw new JsonException($"Cannot convert JSON token {reader.TokenType} to nullable Boolean.");
        }

        public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
        {
            if (value.HasValue) writer.WriteBooleanValue(value.Value);
            else writer.WriteNullValue();
        }
    }
}

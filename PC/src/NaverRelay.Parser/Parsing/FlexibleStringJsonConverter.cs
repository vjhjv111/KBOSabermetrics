using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaverRelay.Parsing
{
    /// <summary>
    /// Historical relay payloads occasionally switch scalar values between JSON strings,
    /// numbers, and booleans. This converter keeps raw string DTO properties readable
    /// without silently discarding the original scalar value.
    /// </summary>
    internal sealed class FlexibleStringJsonConverter : JsonConverter<string?>
    {
        public override bool HandleNull => true;

        public override string? Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options)
        {
            return reader.TokenType switch
            {
                JsonTokenType.Null => null,
                JsonTokenType.String => reader.GetString(),
                JsonTokenType.True => bool.TrueString.ToLowerInvariant(),
                JsonTokenType.False => bool.FalseString.ToLowerInvariant(),
                JsonTokenType.Number => ReadNumber(ref reader),
                _ => throw new JsonException(
                    $"Cannot convert JSON token {reader.TokenType} to a string scalar."),
            };
        }

        public override void Write(
            Utf8JsonWriter writer,
            string? value,
            JsonSerializerOptions options)
        {
            if (value == null)
            {
                writer.WriteNullValue();
                return;
            }

            writer.WriteStringValue(value);
        }

        private static string ReadNumber(ref Utf8JsonReader reader)
        {
            if (reader.TryGetInt64(out var integer))
            {
                return integer.ToString(CultureInfo.InvariantCulture);
            }

            if (reader.TryGetDecimal(out var decimalValue))
            {
                return decimalValue.ToString(CultureInfo.InvariantCulture);
            }

            return reader.GetDouble().ToString("R", CultureInfo.InvariantCulture);
        }
    }
}

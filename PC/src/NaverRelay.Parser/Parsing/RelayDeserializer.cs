using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using NaverRelay.Models;

namespace NaverRelay.Parsing
{
    public static class RelayDeserializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };

        static RelayDeserializer()
        {
            Options.Converters.Add(new FlexibleStringJsonConverter());
            Options.Converters.Add(new FlexibleNullableInt32JsonConverter());
            Options.Converters.Add(new FlexibleNullableDoubleJsonConverter());
            Options.Converters.Add(new FlexibleNullableBooleanJsonConverter());
        }

        public static NaverRelayResponse? Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            return JsonSerializer.Deserialize<NaverRelayResponse>(json, Options);
        }

        public static NaverRelayResponse? DeserializeFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                throw new ArgumentException("A JSON file path is required.", nameof(filePath));
            }

            return Deserialize(File.ReadAllText(filePath));
        }

        public static bool TryDeserialize(string json, out NaverRelayResponse? response, out string? error)
        {
            try
            {
                response = Deserialize(json);
                error = response == null ? "The response was empty." : null;
                return response != null;
            }
            catch (JsonException ex)
            {
                response = null;
                error = $"JSON parse error at {ex.Path ?? "<unknown>"}: {ex.Message}";
                return false;
            }
            catch (Exception ex)
            {
                response = null;
                error = ex.Message;
                return false;
            }
        }
    }
}

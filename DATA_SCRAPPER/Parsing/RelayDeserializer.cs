using System.Text.Json;
using NaverRelayUI.Models;

namespace NaverRelayUI.Parsing
{
    public static class RelayDeserializer
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
        };

        public static NaverRelayResponse? Deserialize(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.TryGetProperty("naver", out var naver))
            {
                if (naver.ValueKind != JsonValueKind.Object) return null;
                return naver.Deserialize<NaverRelayResponse>(Options);
            }
            return root.Deserialize<NaverRelayResponse>(Options);
        }
    }
}

using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop
{
    public sealed class BootResult
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; set; }

        [JsonPropertyName("token")]

        public string? Token { get; set; }

        [JsonPropertyName("mapId")]
        public string? MapId { get; set; }

        [JsonPropertyName("scopeKey")]
        public string? ScopeKey { get; set; }
        [JsonPropertyName("reason")]
        public string? Reason { get; set; }
    }

}

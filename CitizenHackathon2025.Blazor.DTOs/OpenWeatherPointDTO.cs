using System.Text.Json.Serialization;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class OpenWeatherPointDTO
    {
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("lon")] public double Lon { get; set; }
    }
}

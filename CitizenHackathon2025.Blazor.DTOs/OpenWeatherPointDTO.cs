using System.Text.Json.Serialization;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class OpenWeatherPointDTO
    {
        [JsonPropertyName("lat")] public double Lat { get; set; }
        [JsonPropertyName("lon")] public double Lon { get; set; }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
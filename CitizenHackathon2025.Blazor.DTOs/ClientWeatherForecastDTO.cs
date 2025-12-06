using System.Text.Json.Serialization;
using CitizenHackathon2025.Contracts.Enums;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public class ClientWeatherForecastDTO
    {

        public int Id { get; set; }
        public DateTime DateWeather { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public int TemperatureC { get; set; }
        public string? Summary { get; set; }
        public double RainfallMm { get; set; }
        public int Humidity { get; set; }
        public double WindSpeedKmh { get; set; }
        public WeatherType WeatherType { get; set; }

        public bool IsSevere { get; set; }

        // ====== UI fields (non-persistent) ======
        [JsonIgnore] public string WeatherMain { get; set; } = "";
        [JsonIgnore] public string IconUrl { get; set; } = "";
        //[JsonIgnore] public bool IsSevere { get; set; }
        [JsonIgnore] public string Description { get; set; } = "";
        [JsonIgnore] public string Icon { get; set; } = "";
    }
}



































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
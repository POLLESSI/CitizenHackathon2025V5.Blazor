using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientWeatherForecastDTO
    {

        public int Id { get; set; }
        public DateTime DateWeather { get; set; }
        public int TemperatureC { get; set; }
        public string? Summary { get; set; }
        public double RainfallMm { get; set; }
        public int Humidity { get; set; }
        public double WindSpeedKmh { get; set; }

        // ====== UI fields (non-persistent) ======
        [JsonIgnore] public string WeatherMain { get; set; } = "";
        [JsonIgnore] public string IconUrl { get; set; } = "";
        [JsonIgnore] public bool IsSevere { get; set; }
        [JsonIgnore] public string Description { get; set; } = "";
        [JsonIgnore] public string Icon { get; set; } = "";
    }
}






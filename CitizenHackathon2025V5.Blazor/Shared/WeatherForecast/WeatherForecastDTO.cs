namespace CitizenHackathon2025V5.Blazor.Client.Shared.WeatherForecast
{
    public class WeatherForecastDTO
    {
#nullable disable
        public int Id { get; set; }
        public DateTime ForecastTime { get; set; }          // Date/time of forecast
        public double TemperatureC { get; set; }            // °C
        public string Summary { get; set; } = "";           // Ex: "Rain", "Sunny", "Cloudy"
        public int PrecipitationProbability { get; set; }   // %
        public double WindSpeedKmh { get; set; }            // km/h
        public string Icon { get; set; } = "";
    }
}

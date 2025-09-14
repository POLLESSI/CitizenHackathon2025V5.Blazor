namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientWeatherForecastDTO
    {

        public int Id { get; set; }
        public DateTime DateWeather { get; set; }
        public int TemperatureC { get; set; }
        public int TemperatureF { get; set; }
        public string Summary { get; set; } = "";
        public double? RainfallMm { get; set; }
        public int? Humidity { get; set; }
        public double? WindSpeedKmh { get; set; }          // km/h
        public string Icon { get; set; } = "";
    }
}






namespace CitizenHackathon2025V5.Blazor.Client.Models
{
    public class WeatherForecastModel
    {
#nullable disable
        public int Id { get; set; }
        public DateTime DateWeather { get; set; }
        public int TemperatureC { get; set; }
        public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
        public string Summary { get; set; }
        public decimal RainfallNm { get; set; }
        public int Humidity { get; set; }
        public decimal WindSpeedKmh { get; set; }
    }
}












































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/





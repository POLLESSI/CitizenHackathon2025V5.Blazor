namespace CitizenHackathon2025V5.Blazor.Client.Models.OutZen
{
    public class OutZenWeatherBundleModels
    {
        public sealed class OutZenBundleInputWeatherOnly
        {
            public List<OutZenWeatherItem> Weather { get; set; } = new();
        }

        public sealed class OutZenWeatherDelta
        {
            public string Action { get; set; } = "upsert"; // "upsert" | "delete"
            public OutZenWeatherItem? Item { get; set; }   // present if upsert
            public int? Id { get; set; }                   // present if delete
        }

        public sealed class OutZenWeatherItem
        {
            public int Id { get; set; }

            // IMPORTANT: names that match pickLatLng() on the JS side
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }

            public string? Summary { get; set; }
            public string? WeatherType { get; set; }
            public bool IsSevere { get; set; }
            public DateTime DateWeather { get; set; }

            public double TemperatureC { get; set; }
            public double Humidity { get; set; }
            public double WindSpeedKmh { get; set; }
        }
    }
}



































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
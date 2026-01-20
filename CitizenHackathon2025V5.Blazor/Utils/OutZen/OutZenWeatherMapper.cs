using CitizenHackathon2025.Blazor.DTOs;
using static CitizenHackathon2025V5.Blazor.Client.Models.OutZen.OutZenWeatherBundleModels;

namespace CitizenHackathon2025V5.Blazor.Client.Utils.OutZen;

public static class OutZenWeatherMapper
{
    private static double? ToDouble(decimal? v)
        => v.HasValue ? (double)v.Value : (double?)null;

    public static OutZenWeatherItem ToOutZenWeatherItem(ClientWeatherForecastDTO dto)
    {
        return new OutZenWeatherItem
        {
            Id = dto.Id,
            Latitude = dto.Latitude,
            Longitude = dto.Longitude,

            Summary = dto.Summary,
            WeatherType = dto.WeatherType.ToString(),
            IsSevere = dto.IsSevere,
            DateWeather = dto.DateWeather,

            // ⚠️ Warning: your DTO client has TemperatureC in the internal settings.
            // OutZenWeatherItem expects double => cast ok
            TemperatureC = dto.TemperatureC,
            Humidity = dto.Humidity,
            WindSpeedKmh = dto.WindSpeedKmh
        };
    }

    public static OutZenWeatherDelta BuildDelta(ClientWeatherForecastDTO dto)
    {
        if (dto is null)
            return new OutZenWeatherDelta { Action = "delete", Id = 0 };

        var hasCoords =
            !double.IsNaN(dto.Latitude) &&
            !double.IsNaN(dto.Longitude) &&
            dto.Latitude is >= -90 and <= 90 &&
            dto.Longitude is >= -180 and <= 180 &&
            !(dto.Latitude == 0 && dto.Longitude == 0); // optional if 0/0 = invalid on your system

        if (!hasCoords)
            return new OutZenWeatherDelta { Action = "delete", Id = dto.Id };

        return new OutZenWeatherDelta
        {
            Action = "upsert",
            Item = ToOutZenWeatherItem(dto)
        };
    }

}















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
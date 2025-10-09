using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;
using CitizenHackathon2025V5.Blazor.Client.Utils;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class WeatherForecastUiEnricher
    {
        public static ClientWeatherForecastDTO Enrich(ClientWeatherForecastDTO dto)
        {
            // 1) Type from summary
            var type = WeatherForecastSeverityHelper.GetWeatherType(dto);

            // 2) Purely UI fields
            dto.WeatherMain = type.ToString();
            dto.Icon = WeatherForecastSeverityHelper.GetIcon(type);
            dto.IconUrl = IconUrlFrom(type);     // eg: local mapping, not OpenWeather required
            dto.Description = WeatherForecastSeverityHelper.GetDescription(type);

            // 3) Simple severity (examples — adapt your thresholds)
            dto.IsSevere =
                dto.WindSpeedKmh >= 60
                || dto.RainfallMm >= 10
                || type is WeatherType.Thunderstorm or WeatherType.Storm or WeatherType.Blizzard;

            return dto;
        }

        private static string IconUrlFrom(WeatherType type)
        {
            // Put your own pictograms (wwwroot/images/weather/…)
            var name = type.ToString().ToLowerInvariant();
            return $"/images/weather/{name}.svg";
        }
    }
}

























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
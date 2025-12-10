using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Enums;
using CitizenHackathon2025V5.Blazor.Client.Utils;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class WeatherForecastUiEnricher
    {
        public static ClientWeatherForecastDTO Enrich(ClientWeatherForecastDTO dto)
        {
            // 1) Type from summary
            var type = WeatherForecastSeverityHelper.ToEnum(dto.Summary ?? string.Empty); // ✅ remplace GetWeatherType

            // 2) Purely UI
            dto.WeatherMain = type.ToString();
            dto.Icon = WeatherForecastSeverityHelper.GetIcon(type);
            dto.IconUrl = IconUrlFrom(type);
            dto.Description = WeatherForecastSeverityHelper.GetDescription(type);

            // 3) Simple severity
            dto.IsSevere =
                dto.WindSpeedKmh >= 60
                || dto.RainfallMm >= 10
                || type is WeatherType.Thunderstorm or WeatherType.Storm or WeatherType.Blizzard;

            return dto;
        }

        private static string IconUrlFrom(WeatherType type)
            => $"/images/weather/{type.ToString().ToLowerInvariant()}.svg";
    }
}



































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
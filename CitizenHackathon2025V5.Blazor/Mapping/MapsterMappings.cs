using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;
using CitizenHackathon2025V5.Blazor.Client.Shared.CrowdInfo;
using CitizenHackathon2025V5.Blazor.Client.Shared.Suggestion;
using CitizenHackathon2025V5.Blazor.Client.Shared.TrafficCondition;
using CitizenHackathon2025V5.Blazor.Client.Shared.WeatherForecast;
using Mapster;

namespace CitizenHackathon2025V5.Blazor.Client.Mapping
{
    public static class MapsterMapping
    {
        public static void RegisterMappings()
        {
            // -------------------
            // CrowdInfo
            // -------------------
            TypeAdapterConfig<CrowdInfoDTO, CrowdInfoUIDTO>.NewConfig()
                .Map(dest => dest.CrowdLevel, src => MapCrowdLevel(ParseCrowdLevel(src.CrowdLevel)))
                .Map(dest => dest.Color, src => MapCrowdColor(ParseCrowdLevel(src.CrowdLevel)))
                .Map(dest => dest.Icon, src => MapCrowdIcon(ParseCrowdLevel(src.CrowdLevel)));

            // -------------------
            // TrafficCondition
            // -------------------
            TypeAdapterConfig<TrafficConditionDTO, TrafficInfoUIDTO>.NewConfig()
                .Map(dest => dest.Level, src => ParseTrafficLevel(src.Level))
                .Map(dest => dest.Color, src => MapTrafficColor(ParseTrafficLevel(src.Level)))
                .Map(dest => dest.Icon, src => MapTrafficIcon(ParseTrafficLevel(src.Level)));

            // -------------------
            // WeatherForecast
            // -------------------
            TypeAdapterConfig<WeatherForecastDTO, WeatherForecastUIDTO>.NewConfig()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Icon, src => MapWeatherIcon(src.Summary))
                .Map(dest => dest.Color, src => MapWeatherColor(src.Summary));

            // -------------------
            // Suggestion
            // -------------------
            TypeAdapterConfig<SuggestionDTO, SuggestionUIDTO>.NewConfig()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Title, src => src.Title)
                .Map(dest => dest.Latitude, src => src.Latitude)
                .Map(dest => dest.Longitude, src => src.Longitude)
                .Map(dest => dest.DistanceKm, src => src.DistanceKm)
                .Map(dest => dest.Reason, src => src.Reason)
                .Map(dest => dest.SuggestedAlternative, src => src.SuggestedAlternative);
        }

        // ==================================================
        // Helpers - Crowd
        // ==================================================
        private static int ParseCrowdLevel(string level) =>
            int.TryParse(level, out var result) ? result : 1; // Défaut = 1 (faible)

        private static CrowdLevelEnum MapCrowdLevel(int level) =>
            level <= 3 ? CrowdLevelEnum.Low :
            level <= 6 ? CrowdLevelEnum.Medium :
            CrowdLevelEnum.High;

        private static string MapCrowdColor(int level) =>
            level <= 3 ? "green" :
            level <= 6 ? "orange" :
            "red";

        private static string MapCrowdIcon(int level) =>
            level <= 3 ? "smile" :
            level <= 6 ? "neutral" :
            "frown";

        // ==================================================
        // Helpers - Traffic
        // ==================================================
        private static int ParseTrafficLevel(string level) =>
            int.TryParse(level, out var result) ? result : 1; // Défaut = 1 (Low)

        private static string MapTrafficColor(int level) =>
            level <= 3 ? "green" :
            level <= 6 ? "orange" :
            "red";

        private static string MapTrafficIcon(int level) =>
            level <= 3 ? "car" :
            level <= 6 ? "car-side" :
            "car-burst";

        // ==================================================
        // Helpers - Weather
        // ==================================================
        private static string MapWeatherIcon(string summary) =>
            summary?.ToLower() switch
            {
                "sunny" => "sun",
                "clear" => "sun",
                "partlycloudy" => "cloud-sun",
                "cloudy" => "cloud",
                "rain" => "cloud-rain",
                "thunderstorm" => "bolt",
                "snow" => "snowflake",
                _ => "question"
            };

        private static string MapWeatherColor(string summary) =>
            summary?.ToLower() switch
            {
                "sunny" => "yellow",
                "clear" => "yellow",
                "partlycloudy" => "lightgray",
                "cloudy" => "gray",
                "rain" => "blue",
                "thunderstorm" => "purple",
                "snow" => "white",
                _ => "darkgray"
            };
    }
}
























































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V4.Blazor.Client. All rights reserved.*/
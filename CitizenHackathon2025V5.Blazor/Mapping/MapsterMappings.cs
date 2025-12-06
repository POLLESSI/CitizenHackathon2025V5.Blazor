using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Enums;
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
            TypeAdapterConfig<ClientCrowdInfoDTO, CrowdInfoUIDTO>.NewConfig()
                .Map(dest => dest.CrowdLevel, src => MapCrowdLevel(ParseCrowdLevel(src.CrowdLevel)))
                .Map(dest => dest.Color, src => MapCrowdColor(ParseCrowdLevel(src.CrowdLevel)))
                .Map(dest => dest.Icon, src => MapCrowdIcon(ParseCrowdLevel(src.CrowdLevel)));

            // -------------------
            // TrafficConditionDTO
            // -------------------
            TypeAdapterConfig<ClientTrafficConditionDTO, TrafficInfoUIDTO>.NewConfig()
                .Map(dest => dest.Level, src => ParseTrafficLevel(src.CongestionLevel))
                .Map(dest => dest.Color, src => MapTrafficColor(ParseTrafficLevel(src.CongestionLevel)))
                .Map(dest => dest.Icon, src => MapTrafficIcon(ParseTrafficLevel(src.CongestionLevel)));

            // -------------------
            // WeatherForecastDTO
            // -------------------
            TypeAdapterConfig<ClientWeatherForecastDTO, WeatherForecastUIDTO>.NewConfig()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Icon, src => MapWeatherIcon(src.Summary))
                .Map(dest => dest.Color, src => MapWeatherColor(src.Summary));

            // -------------------
            // Suggestion
            // -------------------
            TypeAdapterConfig<ClientSuggestionDTO, SuggestionUIDTO>.NewConfig()
                .Map(dest => dest.Id, src => src.Id)
                .Map(dest => dest.Title, src => src.Title)
                .Map(dest => dest.Latitude, src => src.Latitude)
                .Map(dest => dest.Longitude, src => src.Longitude)
                .Map(dest => dest.DistanceKm, src => src.DistanceKm)
                .Map(dest => dest.Reason, src => src.Reason)
                .Map(dest => dest.SuggestedAlternative, src => src.SuggestedAlternatives);
        }

        // ==================================================
        // Helpers - Crowd
        // ==================================================

        // Overload 1: Source exposes a string ("Low" / "2" / etc.)
        private static CrowdLevelEnum ParseCrowdLevel(string? level)
        {
            if (string.IsNullOrWhiteSpace(level)) return CrowdLevelEnum.Medium;

            if (int.TryParse(level, out var n))
                return ParseCrowdLevel(n);

            return level.Trim().ToLowerInvariant() switch
            {
                "low" => CrowdLevelEnum.Low,
                "medium" => CrowdLevelEnum.Medium,
                "high" => CrowdLevelEnum.High,
                "critical" => CrowdLevelEnum.Critical,
                _ => CrowdLevelEnum.Medium
            };
        }

        // Overload 2: source exposes an int (0..10 or already 0..3)
        private static CrowdLevelEnum ParseCrowdLevel(int level)
        {
            // If it's already enum 0..3, it works out perfectly.
            if (level <= 1) return CrowdLevelEnum.Low;
            if (level == 2 || level == 3) return CrowdLevelEnum.Medium;

            // otherwise, bucketing 0..10 for example.
            if (level <= 3) return CrowdLevelEnum.Low;
            if (level <= 6) return CrowdLevelEnum.Medium;
            if (level <= 8) return CrowdLevelEnum.High;
            return CrowdLevelEnum.Critical;
        }

        // If your UI property expects an int
        private static int MapCrowdLevel(CrowdLevelEnum level) => (int)level;

        private static string MapCrowdColor(CrowdLevelEnum level) => level switch
        {
            CrowdLevelEnum.Low => "green",
            CrowdLevelEnum.Medium => "orange",
            CrowdLevelEnum.High => "red",
            CrowdLevelEnum.Critical => "darkred",
            _ => "gray"
        };

        private static string MapCrowdIcon(CrowdLevelEnum level) => level switch
        {
            CrowdLevelEnum.Low => "smile",
            CrowdLevelEnum.Medium => "neutral",
            CrowdLevelEnum.High => "frown",
            CrowdLevelEnum.Critical => "triangle-exclamation",
            _ => "question"
        };

        // ==================================================
        // Helpers - Traffic
        // ==================================================
        private static int ParseTrafficLevel(string? level) =>
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
        public static string MapWeatherIcon(string? summary)
            => string.IsNullOrWhiteSpace(summary) ? "unknown" :
               summary.ToLower() switch
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
        private static string MapWeatherColor(string? summary)
            => string.IsNullOrWhiteSpace(summary) ? "darkgray" :
               summary.ToLower() switch
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





using CitizenHackathon2025.Contracts.Enums;

namespace CitizenHackathon2025V5.Blazor.Client.Utils
{
    public static class WeatherForecastSeverityHelper
    {
        /// <summary>
        /// Converts a weather summary (string) to WeatherType.
        /// </summary>
        public static WeatherType ToEnum(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return WeatherType.Unknown;

            var s = summary.Trim().ToLowerInvariant();

            return s switch
            {
                "clear" => WeatherType.Clear,
                "sunny" => WeatherType.Sunny,
                "partlycloudy" or "partly cloudy" => WeatherType.PartlyCloudy,
                "cloudy" => WeatherType.Cloudy,
                "overcast" => WeatherType.Overcast,

                "rain" => WeatherType.Rain,
                "drizzle" => WeatherType.Drizzle,

                "storm" => WeatherType.Storm,
                "thunderstorm" => WeatherType.Thunderstorm,

                "snow" => WeatherType.Snow,
                "snowstorm" or "snow storm" => WeatherType.Snowstorm,
                "blizzard" => WeatherType.Blizzard,

                "hail" => WeatherType.Hail,
                "hailstorm" or "hail storm" => WeatherType.Hailstorm,

                "fog" => WeatherType.Fog,
                "mist" => WeatherType.Mist,
                "smoke" => WeatherType.Smoke,

                "blackice" or "black ice" => WeatherType.BlackIce,
                "freezingrain" or "freezing rain" => WeatherType.FreezingRain,

                "windy" => WeatherType.Windy,
                "heatwave" or "heat wave" => WeatherType.Heatwave,
                "coldwave" or "cold wave" => WeatherType.ColdWave,

                "sandstorm" or "sand storm" => WeatherType.Sandstorm,

                "ash" => WeatherType.Ash,
                "volcanicash" or "volcanic ash" => WeatherType.VolcanicAsh,
                "burningclouds" or "burning clouds" => WeatherType.BurningClouds,

                _ => WeatherType.Unknown
            };
        }

        /// <summary>
        /// Returns an indicative color related to the weather type.
        /// </summary>
        public static string GetColor(WeatherType type) =>
            type switch
            {
                WeatherType.Clear or WeatherType.Sunny => "#FFD700",              // Sun yellow
                WeatherType.PartlyCloudy or WeatherType.Cloudy => "#90A4AE",     // Light gray
                WeatherType.Overcast => "#607D8B",                               // Dark gray

                WeatherType.Rain or WeatherType.Drizzle => "#2196F3",            // Rain blue
                WeatherType.Thunderstorm or WeatherType.Storm => "#673AB7",      // Purple

                WeatherType.Snow or WeatherType.Snowstorm or WeatherType.Blizzard => "#E1F5FE", // White/blue
                WeatherType.Hail or WeatherType.Hailstorm => "#B0BEC5",          // Steel gray

                WeatherType.Fog or WeatherType.Mist or WeatherType.Smoke => "#BDBDBD", // Gray

                WeatherType.BlackIce or WeatherType.FreezingRain => "#00BCD4",   // Ice blue

                WeatherType.Heatwave => "#FF5722",                               // Warm orange
                WeatherType.ColdWave => "#3F51B5",                               // Cool blue

                WeatherType.Sandstorm => "#FBC02D",                              // Sand yellow

                WeatherType.Ash or WeatherType.VolcanicAsh or WeatherType.BurningClouds => "#5D4037", // Brown

                _ => "#9E9E9E"                                                   // Neutral/unknown
            };

        /// <summary>
        /// Returns a Unicode icon (or Material/Bootstrap equivalent).
        /// </summary>
        public static string GetIcon(WeatherType type) =>
            type switch
            {
                WeatherType.Clear or WeatherType.Sunny => "☀️",
                WeatherType.PartlyCloudy => "⛅",
                WeatherType.Cloudy or WeatherType.Overcast => "☁️",

                WeatherType.Rain or WeatherType.Drizzle => "🌧️",
                WeatherType.Thunderstorm or WeatherType.Storm => "⛈️",

                WeatherType.Snow or WeatherType.Snowstorm or WeatherType.Blizzard => "❄️",

                WeatherType.Hail or WeatherType.Hailstorm => "🌨️",
                WeatherType.Fog or WeatherType.Mist or WeatherType.Smoke => "🌫️",

                WeatherType.BlackIce or WeatherType.FreezingRain => "🧊",
                WeatherType.Windy => "💨",

                WeatherType.Heatwave => "🔥",
                WeatherType.ColdWave => "🥶",

                WeatherType.Sandstorm => "🏜️",
                WeatherType.Ash or WeatherType.VolcanicAsh or WeatherType.BurningClouds => "🌋",

                _ => "❓"
            };

        /// <summary>
        /// User-readable description.
        /// </summary>
        public static string GetDescription(WeatherType type) =>
            type switch
            {
                WeatherType.Clear => "Clear sky",
                WeatherType.Sunny => "Sunny",
                WeatherType.PartlyCloudy => "Partly cloudy",
                WeatherType.Cloudy => "Cloudy",
                WeatherType.Overcast => "Overcast sky",

                WeatherType.Rain => "Rain",
                WeatherType.Drizzle => "Light rain",

                WeatherType.Storm => "Storm",
                WeatherType.Thunderstorm => "Thunderstorm",

                WeatherType.Snow => "Snow",
                WeatherType.Snowstorm => "Snowstorm",
                WeatherType.Blizzard => "Blizzard",

                WeatherType.Hail => "Hail",
                WeatherType.Hailstorm => "Hailstorm",

                WeatherType.Fog => "Fog",
                WeatherType.Mist => "Haze",
                WeatherType.Smoke => "Smoke",

                WeatherType.BlackIce => "Black ice",
                WeatherType.FreezingRain => "Freezing rain",

                WeatherType.Windy => "Windy",
                WeatherType.Heatwave => "Heat wave",
                WeatherType.ColdWave => "Cold snap",

                WeatherType.Sandstorm => "Sandstorm",

                WeatherType.Ash => "Ashes",
                WeatherType.VolcanicAsh => "Volcanic ash",
                WeatherType.BurningClouds => "Burning clouds",

                _ => "Unknown conditions"
            };
    }
}








































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.






// Services/Extensions/WeatherProviderExtensions.cs
using CitizenHackathon2025.Contracts.Enums;

namespace CitizenHackathon2025V5.Blazor.Client.Services.Extensions;

public static class WeatherProviderExtensions
{
    public static string ToDisplayLabel(this WeatherProvider provider)
        => provider switch
        {
            WeatherProvider.OpenWeather => "OpenWeather",
            WeatherProvider.Generated => "Generated",
            WeatherProvider.Manual => "Manual",
            WeatherProvider.Seed => "Seed",
            _ => "Unknown"
        };
}
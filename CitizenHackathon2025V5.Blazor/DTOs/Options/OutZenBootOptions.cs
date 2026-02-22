namespace CitizenHackathon2025V5.Blazor.Client.DTOs.Options
{
    public sealed record OutZenBootOptions(
        string MapId,
        string ScopeKey,
        double Lat,
        double Lng,
        int Zoom,
        bool EnableChart = false,
        bool Force = false,
        bool EnableWeatherLegend = false,
        bool ResetMarkers = false
    );
}

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
        bool ResetMarkers = false,
        bool EnableHybrid = false,
        bool EnableCluster = false,
        int HybridThreshold = 13
    );
}

















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
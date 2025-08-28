namespace CitizenHackathon2025V5.Blazor.Client.SignalR
{
    /// <summary>Logical name of the hubs exposed by the API.</summary>
    public enum HubName
    {
        Crowd,
        Event,
        Suggestion,
        OutZen,
        Traffic,
        Update,
        User,
        Notification,
        AISuggestion,
        WeatherForecast,
        Place
    }

    /// <summary>HubName mapping table -> API route. Single source of truth.</summary>
    public static class HubRoutes
    {
        // ⚠️ These paths must match EXACTLY those in Program.cs
        // ex: app.MapHub<CrowdHub>("/hubs/crowdHub");
        private static readonly IReadOnlyDictionary<HubName, string> _map = new Dictionary<HubName, string>
        {
            [HubName.Crowd] = "/hubs/crowdHub",
            [HubName.Event] = "/hubs/eventHub",
            [HubName.Suggestion] = "/hubs/suggestionHub",
            [HubName.OutZen] = "/hubs/outzen",              // You fixed /hubs/outzen on the API side
            [HubName.Traffic] = "/hubs/trafficHub",
            [HubName.Update] = "/hubs/updateHub",
            [HubName.User] = "/hubs/userHub",
            [HubName.Notification] = "/hubs/notifications",
            [HubName.AISuggestion] = "/aisuggestionhub",          // as in your Program.cs
            [HubName.WeatherForecast] = "/hubs/weatherforecastHub",
            [HubName.Place] = "/hubs/placeHub"
        };

        public static string GetPath(HubName hub) => _map[hub];
    }
}
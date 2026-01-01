using CitizenHackathon2025.Contracts.Hubs;

namespace CitizenHackathon2025V5.Blazor.Client.SignalR
{
    public enum HubName
    {
        OutZen,
        AntennaConnection,
        Message,
        Crowd,
        Event,
        Notifications,
        Place,
        Suggestions,
        Traffic,
        Update,
        Weather
    }

    public static class HubRoutes
    {
        public static string GetPath(HubName hub) => hub switch
        {
            HubName.OutZen => OutZenHubMethods.HubPath,                         // ex: "/hubs/outzenHub"
            HubName.AntennaConnection => CrowdInfoAntennaConnectionHubMethods.HubPath,     // ex: "/hubs/antenna-connection"
            HubName.Message => MessageHubMethods.HubPath,                        // ex: "/hubs/messageHub"
            HubName.Crowd => CrowdHubMethods.HubPath,                          // ex: "/hubs/crowdHub"
            HubName.Event => EventHubMethods.HubPath,                          // ex: "/hubs/eventHub"
            HubName.Notifications => NotificationHubMethods.HubPath,                   // ex: "/hubs/notificationHub"
            HubName.Place => PlaceHubMethods.HubPath,                          // ex: "/hubs/placeHub"
            HubName.Suggestions => SuggestionHubMethods.HubPath,                     // ex: "/hubs/suggestionHub"
            HubName.Traffic => TrafficConditionHubMethods.HubPath,               // ex: "/hubs/trafficHub"
            HubName.Update => UpdateHubMethods.HubPath,                         // ex: "/hubs/updateHub"
            HubName.Weather => WeatherForecastHubMethods.HubPath,                // ex: "/hubs/weatherforecastHub"
            _ => throw new ArgumentOutOfRangeException(nameof(hub), hub, "Unknown hub")
        };

        public static string BuildUrl(string hubBase, HubName hub)
            => $"{hubBase.TrimEnd('/')}{GetPath(hub)}";
    }
}

























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
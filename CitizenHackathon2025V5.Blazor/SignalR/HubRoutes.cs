namespace CitizenHackathon2025V5.Blazor.Client.SignalR
{
    public enum HubName
    {
        OutZen,
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
            HubName.OutZen => "/hub/outzen",
            HubName.Crowd => "/hubs/crowdHub",
            HubName.Event => "/hubs/eventHub",
            HubName.Notifications => "/hubs/notifications",
            HubName.Place => "/hubs/placeHub",
            HubName.Suggestions => "/hubs/suggestionHub",
            HubName.Traffic => "/hubs/trafficHub",
            HubName.Update => "/hubs/updateHub",
            HubName.Weather => "/hubs/weatherforecastHub",
            _ => throw new ArgumentOutOfRangeException(nameof(hub), hub, null)
        };

        public static string BuildUrl(string hubBase, HubName hub)
            => $"{hubBase.TrimEnd('/')}{GetPath(hub)}";
    }
}






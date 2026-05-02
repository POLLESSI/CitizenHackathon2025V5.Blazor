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
        Weather,
        Gpt
    }

    public static class HubRoutes
    {
        public static string GetPath(HubName hub) => hub switch
        {
            HubName.OutZen => OutZenHubMethods.HubPath,
            HubName.AntennaConnection => CrowdInfoAntennaConnectionHubMethods.HubPath,
            HubName.Message => MessageHubMethods.HubPath,
            HubName.Crowd => CrowdHubMethods.HubPath,
            HubName.Event => EventHubMethods.HubPath,
            HubName.Gpt => GptInteractionHubMethods.HubPath,
            HubName.Notifications => NotificationHubMethods.HubPath,
            HubName.Place => PlaceHubMethods.HubPath,
            HubName.Suggestions => SuggestionHubMethods.HubPath,
            HubName.Traffic => TrafficConditionHubMethods.HubPath,
            HubName.Update => UpdateHubMethods.HubPath,
            HubName.Weather => WeatherForecastHubMethods.HubPath,
            _ => throw new ArgumentOutOfRangeException(nameof(hub), hub, "Unknown hub")
        };

        public static string BuildUrl(string hubBase, HubName hub)
        {
            var basePart = (hubBase ?? string.Empty).Trim().TrimEnd('/');
            var pathPart = NormalizePath(GetPath(hub));

            return $"{basePart}/hubs/{pathPart}";
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new InvalidOperationException("SignalR hub path is empty.");

            var value = path.Trim();

            // tolerate :
            // "gptHub"
            // "/gptHub"
            // "/hubs/gptHub"
            // "hubs/gptHub"
            if (value.StartsWith("/hubs/", StringComparison.OrdinalIgnoreCase))
                value = value["/hubs/".Length..];
            else if (value.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
                value = value["hubs/".Length..];
            else if (value.StartsWith("/"))
                value = value[1..];

            return value;
        }
    }
}

























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
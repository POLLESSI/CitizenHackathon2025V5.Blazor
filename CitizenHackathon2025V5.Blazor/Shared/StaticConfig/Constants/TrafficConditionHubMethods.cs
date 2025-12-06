namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class TrafficConditionHubMethods
    {
        /// <summary>Hub Path — Must match the MapHub on the Blazor side.</summary>
        public const string HubPath = "trafficHub";

        /// <summary>Events sent by the server to clients.</summary>
        public static class ToClient
        {
            /// <summary>
            /// Traffic refresh "ping" notification (without payload).
            /// Exactly matches: Clients.All.SendAsync("notifynewtraffic")
            /// </summary>
            public const string NotifyNewTraffic = "notifynewtraffic";

            // Prepare keys for future developments (optional):
            public const string TrafficUpdated = "trafficUpdated";   // ex: payload DTO
            public const string TrafficCleared = "trafficCleared";   // ex: without payload
        }

        /// <summary>Methods invoked by clients on the hub.</summary>
        public static class FromClient
        {
            /// <summary>
            /// Request for broadcast of a traffic notification.
            /// Current hub signature: Task RefreshTraffic()
            /// </summary>
            public const string RefreshTraffic = "RefreshTraffic";
        }
    }
}

















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
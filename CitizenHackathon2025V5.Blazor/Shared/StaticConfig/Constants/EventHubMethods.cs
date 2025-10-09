namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class EventHubMethods
    {
        /// <summary>
        /// Hub path (must match Blazor-side mapping: app.MapHub<EventHub>("/hubs/eventHub"))
        /// </summary>
        public const string HubPath = "/hubs/eventHub";

        /// <summary>
        /// Calls made by the server to clients (Clients.SendAsync(...))
        /// </summary>
        public static class ToClient
        {
            /// <summary>
            /// Notifies of the arrival of a new event or update.
            /// Recommended payload: string (JSON) or serialized DTO.
            /// </summary>
            public const string NewEvent = "NewEvent";

            // (Examples of future extensions, if you need them)
            public const string EventUpdated = "EventUpdated";   // optional
            public const string EventArchived = "EventArchived";  // optional
            public const string EventsRefreshed = "EventsRefreshed"; // optional
        }

        /// <summary>
        /// Calls made by clients to the server (hubConnection.InvokeAsync(...))
        /// </summary>
        public static class FromClient
        {
            /// <summary>
            /// Asks the server to push a refresh notification.
            /// Hub signature: Task RefreshEvent(string message)
            /// </summary>
            public const string RefreshEvent = "RefreshEvent";
        }
    }
}











































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
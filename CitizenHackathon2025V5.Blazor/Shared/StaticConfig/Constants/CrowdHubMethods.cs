namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class CrowdHubMethods
    {
        /// <summary>
        /// Hub path (must match Blazor-side mapping: app.MapHub<CrowdHub>("/hubs/crowdHub"))
        /// </summary>
        public const string HubPath = "/hubs/crowdHub";

        public static class ToClient 
        {
            public const string NewCrowdInfo = "NewCrowdInfo";

            public const string ReceiveCrowdUpdate = "ReceiveCrowdUpdate";
            public const string CrowdInfoArchived = "CrowdInfoArchived";
            public const string CrowdRefreshRequested = "CrowdRefreshRequested";
        }
        /// <summary>
        /// Calls made by clients to the server (hubConnection.InvokeAsync(...))
        /// </summary>
        public static class FromClient
        {
            /// <summary>
            /// Asks the server to push a refresh notification.
            /// Hub signature: Task RefreshCrowdInfo(string message)
            /// </summary>
            public const string RefreshCrowdInfo = "RefreshCrowdInfo";
        }
    }
}





































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
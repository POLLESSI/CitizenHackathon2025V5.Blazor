namespace CitizenHackathon2025V5.Blazor.Client.Constants
{
    public class SignalRConstants
    {
        public static class CrowdHubRoutes
        {
            // Must match app.MapHub<CrowdHub>("/hubs/crowd") on the API side
            public const string Path = "/hubs/crowd";
        }

        public static class CrowdHubMethods
        {
            // Methods sent by the server to clients (Clients.All.SendAsync(...))
            public const string ReceiveCrowdUpdate = "ReceiveCrowdUpdate";
            public const string CrowdRefreshRequested = "CrowdRefreshRequested";

            // Methods exposed by the Hub that the client can invoke
            public const string RefreshCrowd = "RefreshCrowd";
            public const string SendCrowdUpdate = "SendCrowdUpdate";
        }
    }
}

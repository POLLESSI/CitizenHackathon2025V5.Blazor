namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class GptInteractionHubMethods
    {
        /// <summary>
        /// Hub path. Must match the Blazor's MapHub.
        /// </summary>
        public const string HubPath = "/hubs/gptHub";

        /// <summary>
        /// Events sent by the server to clients.
        /// </summary>
        public static class ToClient
        {
            /// <summary>
            /// Notification of new message / GPT update.
            /// Matches: Clients.All.SendAsync("notifynewGPT", payload)
            /// </summary>
            public const string NotifyNewGpt = "notifynewGPT";

            // Possible extensions if needed later:
            public const string ConversationUpdated = "gptConversationUpdated"; // optional
            public const string ConversationCleared = "gptConversationCleared"; // optional
        }

        /// <summary>
        /// Methods invoked by clients on the server (hubConnection.InvokeAsync).
        /// </summary>
        public static class FromClient
        {
            /// <summary>
            /// GPT refresh/broadcast request.
            /// Hub signature: Task RefreshGPT(string message)
            /// </summary>
            public const string RefreshGpt = "RefreshGPT";
        }
    }
}







































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
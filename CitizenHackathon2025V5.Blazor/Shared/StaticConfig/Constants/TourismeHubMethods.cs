namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class TourismeHubMethods
    {
        /// <summary>
        /// Hub path (group /hubs + map "/aisuggestionhub" => "/hubs/aisuggestionhub").
        /// See Program.cs: hubs.MapHub<AISuggestionHub>("/aisuggestionhub");
        /// </summary>
        public const string HubPath = "/hubs/aisuggestionhub";

        /// <summary>Events sent by the server to clients.</summary>
        public static class ToClient
        {
            /// <summary>Suggestion list (payload: JSON string, DTO or serialized list).</summary>
            public const string SuggestionsUpdated = "SuggestionsUpdated";

            /// <summary>Information/status message (payload: string).</summary>
            public const string Info = "TourismInfo";

            /// <summary>Error on AI service side (payload: string).</summary>
            public const string Error = "TourismError";
        }

        /// <summary>Methods invoked by clients to the server.</summary>
        public static class FromClient
        {
            /// <summary>
            /// Request for tourist suggestions (payload: string prompt).
            /// Recommended hub-side signature: Task RequestSuggestions(string prompt)
            /// </summary>
            public const string RequestSuggestions = "RequestSuggestions";
        }
    }
}



















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
namespace CitizenHackathon2025.Shared.StaticConfig.Constants
{
    public static class SuggestionHubMethods
    {
        // if you use MapGroup("/hubs") Blazor side → RELATIVE path recommended :
        public const string HubPath = "/hubs/suggestionHub";
        public static class ToClient
        {
            public const string NewSuggestion = "NewSuggestion";
            public const string ReceiveSuggestion = "ReceiveSuggestion";
        }
        public static class FromClient
        {
            public const string RefreshSuggestion = "RefreshSuggestion";
            public const string SendSuggestion = "SendSuggestion";
        }
    }
}















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
namespace CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants
{
    public static class CrowdCalendarHubMethods
    {
        public const string HubPath = "/hubs/crowd-calendar";
        public const string ReceiveAdvisory = "ReceiveAdvisory";
        public const string ReceiveAdvisories = "ReceiveAdvisories";
        public const string ReceiveCalendarUpdated = "ReceiveCalendarUpdated";

        public static string RegionGroup(string regionCode) =>
            $"region:{regionCode?.Trim().ToUpperInvariant()}";

        public static string PlaceGroup(int placeId) =>
            $"place:{placeId}";
    }
}
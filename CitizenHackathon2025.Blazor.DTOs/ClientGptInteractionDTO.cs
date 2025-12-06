namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class ClientGptInteractionDTO
    {
        public int Id { get; set; }
        public string? Prompt { get; set; }
        public string? Response { get; set; }
        public string? PromptHash { get; set; }
        public DateTime CreatedAt { get; set; }
        public bool Active { get; set; }

        public int? EventId { get; set; }
        public int? CrowdInfoId { get; set; }
        public int? PlaceId { get; set; }
        public int? TrafficConditionId { get; set; }
        public int? WeatherForecastId { get; set; }

        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public string? SourceType { get; set; }
        public int? CrowdLevel { get; set; }
    }
}































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
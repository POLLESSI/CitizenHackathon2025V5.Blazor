namespace CitizenHackathon2025.Blazor.DTOs
{
    public class ClientMessageDTO
    {
        public int Id { get; set; }
        public string? UserId { get; set; }
        public string? SourceType { get; set; }
        public int? SourceId { get; set; }
        public string? RelatedName { get; set; }
        public decimal? Latitude { get; set; }
        public decimal? Longitude { get; set; }
        public string? Tags { get; set; }
        public string Content { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }

}














































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
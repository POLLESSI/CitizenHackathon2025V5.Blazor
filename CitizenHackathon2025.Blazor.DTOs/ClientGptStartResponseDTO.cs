namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class ClientGptStartResponseDTO
    {
        public bool Accepted { get; set; }
        public int InteractionId { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public DateTime StartedAtUtc { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
    }
}
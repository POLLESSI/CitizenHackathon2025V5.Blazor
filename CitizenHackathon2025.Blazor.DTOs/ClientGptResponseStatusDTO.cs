namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class ClientGptResponseStatusDTO
    {
        public int InteractionId { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? Message { get; set; }
        public DateTime TimestampUtc { get; set; }

        public bool IsTerminal =>
            string.Equals(Status, "completed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "failed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Status, "cancelled", StringComparison.OrdinalIgnoreCase);
    }
}
































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
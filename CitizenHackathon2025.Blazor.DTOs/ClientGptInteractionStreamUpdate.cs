namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class ClientGptInteractionStreamUpdate
    {
        public int InteractionId { get; set; }
        public string RequestId { get; set; } = string.Empty;
        public string Chunk { get; set; } = string.Empty;
        public bool IsFinal { get; set; }
        public string AggregatedResponse { get; set; } = string.Empty;
    }
}





































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
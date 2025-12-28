namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class ClientEventAntennaCrowdDTO
    {
        public int EventId { get; set; }
        public int AntennaId { get; set; }
        public double DistanceMeters { get; set; }
        public ClientAntennaCountsDTO Counts { get; set; } = new();
    }
}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
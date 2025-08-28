namespace CitizenHackathon2025V5.Blazor.Client.Shared.CrowdInfo
{
    public class CrowdInfoDTO
    {
#nullable disable
        public int Id { get; set; }                         // Unique identifier
        public string LocationName { get; set; } = "";      // Location name
        public string Latitude { get; set; }                // Geo position
        public string Longitude { get; set; }
        public string CrowdLevel { get; set; }                 // Crowd level (0-10)
        public DateTime Timestamp { get; set; }             // UTC Timestamp
        public double Density { get; set; }                 // Persons per m², optional

        public string Source { get; set; } = "";            // "Simulation", "Sensor", "User", ...

    }
}

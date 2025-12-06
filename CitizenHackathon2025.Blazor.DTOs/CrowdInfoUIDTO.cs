//using CitizenHackathon2025V5.Blazor.Client.Enums;

namespace CitizenHackathon2025.Blazor.DTOs
{
    public class CrowdInfoUIDTO
    {
        public int Id { get; set; }                   // Unique identifier
        public string PlaceName { get; set; } = "";    // Name of the place concerned
        public string Icon { get; set; } = "";         // Icon to display (name or image path)
        public string Color { get; set; } = "";        // CSS color code or name (eg: "red", "#FF0000")
        public int CrowdLevel { get; set; }   // Crowd level (e.g. "Low", "Moderate", "High")
        public DateTime Timestamp { get; set; }        // Date/time of the information
        public string Source { get; set; } = "";       // Data source (e.g. "Simulation", "User", etc.)
        public double Latitude { get; set; }           // Geographic coordinates
        public double Longitude { get; set; }
    }
}









































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/





namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientTrafficConditionDTO
    {
#nullable disable
        public int Id { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime DateCondition { get; set; }
        public string IncidentType { get; set; } = "";
        public string Description { get; set; } = "";
        public string CongestionLevel { get; set; } = "";
        public string? Location { get; set; } = "";
        public byte? Level { get; set; }
        public string? Message { get; set; } = "";         // Severity level (e.g. "Low", "Moderate", "High")

        public int DelayInSeconds { get; set; }             // Duration/impact if available

    }
}






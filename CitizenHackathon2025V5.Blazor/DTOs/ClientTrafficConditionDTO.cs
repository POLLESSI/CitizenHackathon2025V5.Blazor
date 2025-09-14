namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientTrafficConditionDTO
    {
#nullable disable
        public string Id { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime DateCondition { get; set; }
        public string IncidentType { get; set; } = "";        // Jam, Accident, Roadwork, ...
        public string Description { get; set; } = "";
        public string Level { get; set; } = "";            // Severity level (e.g. "Low", "Moderate", "High")

        public int DelayInSeconds { get; set; }             // Duration/impact if available

    }
}






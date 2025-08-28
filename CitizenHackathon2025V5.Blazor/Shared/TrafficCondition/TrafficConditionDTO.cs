namespace CitizenHackathon2025V5.Blazor.Client.Shared.TrafficCondition
{
    public class TrafficConditionDTO
    {
#nullable disable
        public string Id { get; set; } = default!;
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public DateTime DateCondition { get; set; }
        public string IncidentType { get; set; } = string.Empty;        // Jam, Accident, Roadwork, ...
        public string Description { get; set; } = string.Empty;
        public string Level { get; set; } = string.Empty;            // Severity level (e.g. "Low", "Moderate", "High")

        public int DelayInSeconds { get; set; }             // Duration/impact if available

    }
}

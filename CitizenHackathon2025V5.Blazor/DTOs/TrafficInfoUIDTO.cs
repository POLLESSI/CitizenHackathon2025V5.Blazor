namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class TrafficInfoUIDTO
    {
        public string Id { get; set; } = "";           // Attention : DTO utilise string
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public string Level { get; set; } = "";        // Niveau en string ("Low", "Medium", "High")
        public string Color { get; set; } = "";
        public string Icon { get; set; } = "";
        public DateTime DateCondition { get; set; }
        public string Description { get; set; } = "";
        public string IncidentType { get; set; } = "";
        public int DelayInSeconds { get; set; }
    }
}

















































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/





namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientCrowdInfoDTO
    {
        public int Id { get; set; }
        public string LocationName { get; set; } = "";
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int CrowdLevel { get; set; }
        public DateTime Timestamp { get; set; }
    }
}






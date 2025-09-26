namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientPlaceDTO
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";

        public string Type { get; set; } = "";
        public bool Indoor { get; set; }
        public double Latitude { get; set; }
        public double Longitude { get; set; }
        public int? Capacity { get; set; }
        public string Tag { get; set; } = "";
    }
}

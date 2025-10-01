namespace CitizenHackathon2025V5.Blazor.Client.DTOs
{
    public class ClientUserDTO
    {
    #nullable disable
        public int Id { get; set; }
        public string Email { get; set; }
        public string PasswordHash { get; set; }
        public string SecurityStamp { get; set; }
        public string Role { get; set; }
        public int Status { get; set; }
        public bool Active { get; set; }
    }
}

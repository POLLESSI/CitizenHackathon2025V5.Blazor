namespace CitizenHackathon2025.Blazor.DTOs
{
    public class ClientGptAnswerDTO
    {
        public int? Id { get; set; }
        public string Prompt { get; set; } = "";
        public string Response { get; set; } = "";
        public DateTime CreatedAt { get; set; }
    }
}

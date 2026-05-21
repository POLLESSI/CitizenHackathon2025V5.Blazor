namespace CitizenHackathon2025.Blazor.DTOs
{
    public sealed class AuthLoginResponseDTO
    {
        public string AccessToken { get; set; } = "";
        public string RefreshToken { get; set; } = "";
    }
}

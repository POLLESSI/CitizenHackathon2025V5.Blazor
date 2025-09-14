namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public interface IAuthService
    {
        Task<string?> GetAccessTokenAsync();
        Task<string?> GetRefreshTokenAsync();
    }
}

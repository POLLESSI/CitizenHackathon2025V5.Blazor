namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public interface IHubTokenService
    {
        Task<string?> GetHubTokenAsync(CancellationToken ct = default);
    }
}


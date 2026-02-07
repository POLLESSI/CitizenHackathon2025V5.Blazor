namespace CitizenHackathon2025V5.Blazor.Client.Services.Interfaces
{
    public interface IHubTokenService
    {
        Task<string?> GetHubTokenAsync(CancellationToken ct = default);
        Task<string?> GetHubAccessTokenAsync();
    }
}











































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
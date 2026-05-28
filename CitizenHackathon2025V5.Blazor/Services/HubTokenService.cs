using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class HubTokenService : IHubTokenService
    {
        private readonly IHttpClientFactory _factory;
        private readonly IAuthService _auth;

        public HubTokenService(IHttpClientFactory factory, IAuthService auth)
        {
            _factory = factory;
            _auth = auth;
        }

        public async Task<string?> GetHubTokenAsync(CancellationToken ct = default)
        {
            var token = await _auth.GetAccessTokenAsync();
            Console.WriteLine($"[HubTokenService] access_token present = {!string.IsNullOrWhiteSpace(token)}");

            // endpoint is mapped outside /api → client "ApiRootAuth"
            var http = _factory.CreateClient("ApiRootWithAuth");

            using var resp = await http.GetAsync("auth/hub-token", ct);
            Console.WriteLine($"[HubTokenService] /auth/hub-token status = {(int)resp.StatusCode}");

            if (!resp.IsSuccessStatusCode) return null;

            var dto = await resp.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct);
            return dto?.Token;
        }
        public Task<string?> GetHubAccessTokenAsync() => _auth.GetAccessTokenAsync();
        private sealed class TokenDto { public string? Token { get; set; } }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
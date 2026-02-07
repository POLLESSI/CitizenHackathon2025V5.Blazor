using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using System.Net.Http.Json;
using static System.Net.WebRequestMethods;

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
            // endpoint est mappé hors /api → client "ApiRootAuth"
            var http = _factory.CreateClient("ApiRootAuth");
            using var resp = await http.GetAsync("auth/hub-token", ct);
            if (!resp.IsSuccessStatusCode) return null;

            var dto = await resp.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct);
            return dto?.Token;
        }
        public Task<string?> GetHubAccessTokenAsync() => _auth.GetAccessTokenAsync();
        private sealed class TokenDto { public string? Token { get; set; } }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
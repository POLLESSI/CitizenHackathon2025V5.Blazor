using System.Net.Http.Json;
using static System.Net.WebRequestMethods;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class HubTokenService : IHubTokenService
    {
        private readonly IHttpClientFactory _factory;
        public HubTokenService(IHttpClientFactory factory) => _factory = factory;

        public async Task<string?> GetHubTokenAsync(CancellationToken ct = default)
        {
            // endpoint est mappé hors /api → client "ApiRootAuth"
            var http = _factory.CreateClient("ApiRootAuth");
            using var resp = await http.GetAsync("auth/hub-token", ct);
            if (!resp.IsSuccessStatusCode) return null;

            var dto = await resp.Content.ReadFromJsonAsync<TokenDto>(cancellationToken: ct);
            return dto?.Token;
        }

        private sealed class TokenDto { public string? Token { get; set; } }
    }
}

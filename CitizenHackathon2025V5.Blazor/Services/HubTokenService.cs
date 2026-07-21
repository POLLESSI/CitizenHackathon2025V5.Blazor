using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components.WebAssembly.Http;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class HubTokenService : IHubTokenService
    {
        private readonly HttpClient _httpClient;
        private readonly IJSRuntime _js;

        private string? _cachedHubToken;
        private DateTimeOffset _cachedHubTokenExpiresAtUtc;

        public HubTokenService(IHttpClientFactory httpClientFactory, IJSRuntime js)
        {
            _httpClient = httpClientFactory.CreateClient("ApiRootWithAuth");

            _js = js;
        }

        public async Task<string?> GetHubTokenAsync(CancellationToken ct = default)
        {
            if (!string.IsNullOrWhiteSpace(_cachedHubToken) && DateTimeOffset.UtcNow < _cachedHubTokenExpiresAtUtc)
            {
                return _cachedHubToken;
            }

            string? localAccessToken = null;

            try
            {
                localAccessToken = await _js.InvokeAsync<string?>("localStorage.getItem", "access_token");
            }
            catch (JSException ex)
            {
                Console.Error.WriteLine($"[HubTokenService] localStorage unavailable: {ex.Message}");
            }

            Console.WriteLine("[HubTokenService] localStorage access_token present = " + !string.IsNullOrWhiteSpace(localAccessToken));

            if (!string.IsNullOrWhiteSpace(localAccessToken))
                return localAccessToken;

            using var request = new HttpRequestMessage(HttpMethod.Get, "auth/hub-token");

            request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            Console.WriteLine($"[HubTokenService] /auth/hub-token status = {(int)response.StatusCode}");

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine($"[HubTokenService] Token request failed: {response.StatusCode}");

                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<HubTokenResponse>(cancellationToken: ct);

            var hubToken = payload?.Token?.Trim();

            Console.WriteLine("[HubTokenService] ephemeral hub token received = " + !string.IsNullOrWhiteSpace(hubToken));

            if (string.IsNullOrWhiteSpace(hubToken))
                return null;

            _cachedHubToken = hubToken;

            // The server currently generates a JWT valid for five minutes.
            // One minute of safety margin.
            _cachedHubTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddMinutes(4);

            return _cachedHubToken;
        }

        // Compatibility with the old interface method.
        public Task<string?> GetHubAccessTokenAsync()
        {
            return GetHubTokenAsync();
        }

        private sealed class HubTokenResponse
        {
            public string? Token { get; set; }
        }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
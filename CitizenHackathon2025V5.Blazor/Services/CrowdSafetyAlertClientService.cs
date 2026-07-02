using System.Net.Http.Json;
using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CrowdSafetyAlertClientService
    {
        private readonly HttpClient _http;

        public CrowdSafetyAlertClientService(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("ApiWithAuth");
        }

        public async Task<List<ClientCrowdSafetyAlertDTO>> GetLatestAsync(
            int limit = 20,
            CancellationToken ct = default)
        {
            return await _http.GetFromJsonAsync<List<ClientCrowdSafetyAlertDTO>>(
                $"crowd/safety-alerts/latest?limit={limit}",
                ct) ?? new();
        }
    }
}
































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
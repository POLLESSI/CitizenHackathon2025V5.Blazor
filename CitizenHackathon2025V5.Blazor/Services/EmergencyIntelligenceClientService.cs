using CitizenHackathon2025V5.Blazor.Client.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class EmergencyIntelligenceClientService
    {
        private readonly HttpClient _httpClient;

        public EmergencyIntelligenceClientService(IHttpClientFactory httpClientFactory)
        {
            ArgumentNullException.ThrowIfNull(httpClientFactory);

            _httpClient = httpClientFactory.CreateClient("EmergencyApi");
        }

        public async Task<EmergencySourceCheckDTO?>CheckSourceAsync(CancellationToken cancellationToken = default)
        {
            using var response = await _httpClient.GetAsync("_diag/emergency/source", cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<EmergencySourceCheckDTO>(cancellationToken: cancellationToken);
        }

        public async Task<EmergencySyncResultDTO?>SynchronizeAsync(CancellationToken cancellationToken = default)
        {
            using var response =await _httpClient.PostAsync("_diag/emergency/sync", content: null, cancellationToken);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<EmergencySyncResultDTO>(cancellationToken: cancellationToken);
        }
    }
}














































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
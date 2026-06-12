using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class TrafficCriticalAlertClientService : ITrafficCriticalAlertClientService
    {
        private readonly HttpClient _http;

        public TrafficCriticalAlertClientService(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("ApiWithAuth");
        }

        public async Task<TrafficAlertResultDTO> SendCriticalTrafficAlertAsync(
            decimal latitude,
            decimal longitude,
            TrafficLevel trafficLevel,
            string description,
            CancellationToken ct = default)
        {
            var payload = new ManualTrafficAlertDTO
            {
                Latitude = latitude,
                Longitude = longitude,
                TrafficLevel = trafficLevel,
                IncidentType = "Critical congestion",
                Description = description
            };

            using var response = await _http.PostAsJsonAsync(
                "TrafficCondition/manual-critical-alert",
                payload,
                ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new TrafficAlertResultDTO
                {
                    Ok = false,
                    Status = "Error",
                    Error = $"HTTP {(int)response.StatusCode}: {body}"
                };
            }

            return await response.Content.ReadFromJsonAsync<TrafficAlertResultDTO>(cancellationToken: ct)
                   ?? new TrafficAlertResultDTO
                   {
                       Ok = true,
                       Status = "Confirmed",
                       ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
                   };
        }
    }
}












































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
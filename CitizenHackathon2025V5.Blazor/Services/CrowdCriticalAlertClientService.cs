using CitizenHackathon2025.Contracts.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CrowdCriticalAlertClientService
    {
        private readonly HttpClient _http;

        public CrowdCriticalAlertClientService(HttpClient http)
        {
            _http = http;
        }

        public async Task<ManualCriticalAlertResultDTO> SendCriticalAlertAsync(
    int placeId,
    string? reason = null,
    CancellationToken ct = default)
        {
            var payload = new ManualCrowdCriticalAlertRequest
            {
                PlaceId = placeId,
                Reason = reason ?? "Manual critical crowd alert from OutZen UI",
                Source = "BlazorEmergencyButton"
            };

            var response = await _http.PostAsJsonAsync(
                "CrowdInfo/manual-critical-alert",
                payload,
                ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new ManualCriticalAlertResultDTO
                {
                    Ok = false,
                    Error = $"HTTP {(int)response.StatusCode}: {body}",
                    Status = "Error"
                };
            }

            var result = await response.Content.ReadFromJsonAsync<ManualCriticalAlertResultDTO>(
                cancellationToken: ct);

            return result ?? new ManualCriticalAlertResultDTO
            {
                Ok = true,
                Status = "Confirmed",
                ConfirmationCount = 1,
                RequiredCount = 1,
                ExpiresAtUtc = DateTime.UtcNow.AddMinutes(5)
            };
        }
    }
}


















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
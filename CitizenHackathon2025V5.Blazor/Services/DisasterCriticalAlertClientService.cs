using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class DisasterCriticalAlertClientService : IDisasterCriticalAlertClientService
    {
        private readonly HttpClient _http;
        private readonly IDeviceIdentityService _deviceIdentity;

        public DisasterCriticalAlertClientService(HttpClient http, IDeviceIdentityService deviceIdentity)
        {
            _http = http;
            _deviceIdentity = deviceIdentity;
        }

        public async Task<DisasterAlertResultDTO> SendCriticalDisasterAlertAsync(
            decimal latitude,
            decimal longitude,
            string? placeName,
            DisasterType disasterType,
            string description,
            CancellationToken ct = default)
        {
            var payload = new ManualDisasterAlertDTO
            {
                Latitude = latitude,
                Longitude = longitude,
                PlaceName = placeName,
                DisasterType = disasterType,
                Severity = 4,
                Description = description,
                DeviceId = await _deviceIdentity.GetDeviceIdAsync()
            };

            using var response = await _http.PostAsJsonAsync(
                "DisasterAlert/manual-critical-alert",
                payload,
                ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                return new DisasterAlertResultDTO
                {
                    Ok = false,
                    Status = "Error",
                    Error = $"HTTP {(int)response.StatusCode}: {body}"
                };
            }

            return await response.Content.ReadFromJsonAsync<DisasterAlertResultDTO>(cancellationToken: ct)
                   ?? new DisasterAlertResultDTO
                   {
                       Ok = false,
                       Status = "Error",
                       Error = "Empty response."
                   };
        }
    }
}






































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
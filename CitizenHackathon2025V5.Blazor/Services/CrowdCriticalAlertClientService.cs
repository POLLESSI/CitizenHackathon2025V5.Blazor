using CitizenHackathon2025.Contracts.DTOs;
using System.Security.Cryptography;
using Microsoft.JSInterop;
using System.Net.Http.Json;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CrowdCriticalAlertClientService
    {
        private readonly HttpClient _http;
        private readonly IJSRuntime _js;

        public CrowdCriticalAlertClientService(HttpClient http, IJSRuntime js)
        {
            _http = http;
            _js = js;
        }

        public async Task<ManualCriticalAlertResultDTO>SendCriticalAlertAsync(int placeId, string reason, CancellationToken ct = default)
        {
            /*
             * Important :
             * le DeviceId est relu au moment exact
             * de chaque clic.
             */
            var deviceId = await _js.InvokeAsync<string>("OutZenDevice.getOrCreateDeviceId");

            if (string.IsNullOrWhiteSpace(deviceId))
            {
                return new ManualCriticalAlertResultDTO
                {
                    Ok = false,
                    Status = "Error",
                    Error =
                        "Unable to resolve device identifier."
                };
            }

            deviceId = deviceId.Trim();

            var deviceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(deviceId)));

            Console.WriteLine(
                "[CrowdCriticalAlertClient V3] " +
                $"Device={deviceId}, " +
                $"Hash={deviceHash[..12]}, " +
                $"PlaceId={placeId}");

            var request =new ManualCrowdCriticalAlertRequest
                {
                    PlaceId =
                        placeId,

                    DeviceId =
                        deviceId,

                    Reason =
                        reason,

                    Source =
                        "ControlCenter"
                };

            using var response = await _http.PostAsJsonAsync("CrowdInfo/manual-critical-alert", request, ct);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync(ct);

                return new ManualCriticalAlertResultDTO
                {
                    Ok = false,
                    Status = "Error",
                    Error = error
                };
            }

            return await response.Content.ReadFromJsonAsync<ManualCriticalAlertResultDTO>(cancellationToken: ct)
                ??
                new ManualCriticalAlertResultDTO
                {
                    Ok = false,
                    Status = "Error",
                    Error =
                        "The API returned an empty response."
                };
        }
    }
}


















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
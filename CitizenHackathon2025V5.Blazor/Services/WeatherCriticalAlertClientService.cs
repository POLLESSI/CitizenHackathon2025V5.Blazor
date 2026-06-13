using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class WeatherCriticalAlertClientService : IWeatherCriticalAlertClientService
    {
        private readonly HttpClient _http;
        private readonly IDeviceIdentityService _deviceIdentity;

        public WeatherCriticalAlertClientService(HttpClient http, IDeviceIdentityService deviceIdentity)
        {
            _http = http;
            _deviceIdentity = deviceIdentity;
        }

        public async Task<WeatherAlertResultDTO> SendCriticalWeatherAlertAsync(decimal latitude, decimal longitude, WeatherType weatherType, string description)
        {
            var payload = new ManualWeatherAlertDTO
            {
                Latitude = latitude,
                Longitude = longitude,
                WeatherType = weatherType,
                Severity = SeverityLevel.Critical,
                Description = description,
                DeviceId = await _deviceIdentity.GetDeviceIdAsync()
            };

            var response = await _http.PostAsJsonAsync(
                "WeatherForecast/manual-critical-alert",
                payload);

            var body = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                return new WeatherAlertResultDTO
                {
                    Ok = false,
                    Status = "Error",
                    Error = $"HTTP {(int)response.StatusCode}: {body}"
                };
            }

            return await response.Content.ReadFromJsonAsync<WeatherAlertResultDTO>()
                   ?? new WeatherAlertResultDTO
                   {
                       Ok = true,
                       Status = "Confirmed"
                   };
        }
    }
}






























































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
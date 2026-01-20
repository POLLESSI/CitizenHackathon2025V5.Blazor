using CitizenHackathon2025.Blazor.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class WeatherForecastApiClient
    {
        private readonly HttpClient _http;
        public WeatherForecastApiClient(HttpClient http) => _http = http;

        public Task<List<ClientWeatherForecastDTO>?> GetCurrentAsync(CancellationToken ct = default)
            => _http.GetFromJsonAsync<List<ClientWeatherForecastDTO>>("api/WeatherForecast/current", ct);

        public async Task PullAsync(decimal lat, decimal lon, CancellationToken ct = default)
        {
            var url = $"api/WeatherForecast/pull?lat={lat}&lon={lon}";
            using var resp = await _http.PostAsync(url, null, ct);
            resp.EnsureSuccessStatusCode();
        }
    }

}







































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
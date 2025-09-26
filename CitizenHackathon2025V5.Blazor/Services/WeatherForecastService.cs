using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class WeatherForecastService
    {
        private readonly HttpClient _http;
        

        public WeatherForecastService(IHttpClientFactory factory)
        {
            _http = factory.CreateClient("ApiWithAuth"); // Base = https://localhost:7254/api/
            Console.WriteLine($"[WF] BaseAddress = {_http.BaseAddress}");
        }

        // GET /api/WeatherForecast/all
        public async Task<List<ClientWeatherForecastDTO>> GetAllAsync()
        {
            var list = await _http.GetFromJsonAsync<List<ClientWeatherForecastDTO>>("WeatherForecast/all")
                       ?? new List<ClientWeatherForecastDTO>();
            return list.Select(WeatherForecastUiEnricher.Enrich).ToList();
        }

        // GET /api/WeatherForecast/current
        public async Task<IEnumerable<ClientWeatherForecastDTO>> GetLatestWeatherForecastAsync()
        {
            try
            {
                var res = await _http.GetAsync("WeatherForecast/current");
                res.EnsureSuccessStatusCode();
                var items = await res.Content.ReadFromJsonAsync<IEnumerable<ClientWeatherForecastDTO>>()
                    ?? Enumerable.Empty<ClientWeatherForecastDTO>();

                return items.Select(WeatherForecastUiEnricher.Enrich).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestWeatherForecastAsync: {ex.Message}");
                return Enumerable.Empty<ClientWeatherForecastDTO>();
            }
        }

        // GET /api/WeatherForecast/current but filter out nulls
        public async Task<List<ClientWeatherForecastDTO>> GetLatestForecastNonNullAsync()
        {
            var raw = await GetLatestWeatherForecastAsync();
            return raw.ToNonNullList(); 
        }

        // POST /api/WeatherForecast/manual   
        public async Task<ClientWeatherForecastDTO?> CreateWeatherForecastAsync(ClientWeatherForecastDTO dto, CancellationToken ct = default)
        {
            var resp = await _http.PostAsJsonAsync("WeatherForecast/manual", dto, ct);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ClientWeatherForecastDTO>(cancellationToken: ct);
        }

        // GET /api/WeatherForecast/history?limit=...
        public async Task<List<ClientWeatherForecastDTO>> GetHistoryAsync(int limit = 128)
        {
            var list = await _http.GetFromJsonAsync<List<ClientWeatherForecastDTO>>($"WeatherForecast/history?limit={limit}")
                       ?? new List<ClientWeatherForecastDTO>();
            return list;
        }

        // (optional) if you add an [HttpPost] without a specific route on the API side
        public async Task<ClientWeatherForecastDTO?> SaveWeatherForecastAsync(ClientWeatherForecastDTO dto)
        {
            var resp = await _http.PostAsJsonAsync("WeatherForecast", dto);
            if (!resp.IsSuccessStatusCode) return null;
            return await resp.Content.ReadFromJsonAsync<ClientWeatherForecastDTO>();
        }
        public async Task<ClientWeatherForecastDTO?> GenerateNewForecastAsync(CancellationToken ct = default)
        {
            try
            {
                // POST /api/WeatherForecast/generate
                var resp = await _http.PostAsync("WeatherForecast/generate", content: null, ct);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientWeatherForecastDTO>(cancellationToken: ct);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GenerateNewForecastAsync: {ex.Message}");
                return null;
            }
        }
        public async Task<ClientWeatherForecastDTO?> UpdateAsync(ClientWeatherForecastDTO dto, CancellationToken ct = default)
        {
            // We create a minimal payload corresponding to the update server DTO
            var payload = new
            {
                dto.Id,
                dto.DateWeather,
                dto.TemperatureC,
                dto.Summary,
                dto.RainfallMm,
                dto.Humidity,
                dto.WindSpeedKmh
            };

            var resp = await _http.PutAsJsonAsync($"WeatherForecast/{dto.Id}", payload, ct);
            if (!resp.IsSuccessStatusCode) return null;

            var updated = await resp.Content.ReadFromJsonAsync<ClientWeatherForecastDTO>(cancellationToken: ct);
            return updated is null ? null : WeatherForecastUiEnricher.Enrich(updated);
        }
    }
}

















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





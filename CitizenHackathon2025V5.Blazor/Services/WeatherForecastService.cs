using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.JSInterop;
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
            try
            {
                var list = await _http.GetFromJsonAsync<List<ClientWeatherForecastDTO>>("WeatherForecast/all")
                           ?? new List<ClientWeatherForecastDTO>();
                return list.Select(WeatherForecastUiEnricher.Enrich).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WF] GetAllAsync failed: {ex.Message}");
                return new List<ClientWeatherForecastDTO>();
            }
        }

        // GET /api/WeatherForecast/current
        public async Task<List<ClientWeatherForecastDTO>> GetLatestWeatherForecastAsync(CancellationToken ct = default)
        {
            try
            {
                using var res = await _http.GetAsync("WeatherForecast/current", ct);

                if (res.StatusCode == HttpStatusCode.NotFound)
                    return new();

                res.EnsureSuccessStatusCode();

                var items = await res.Content
                    .ReadFromJsonAsync<List<ClientWeatherForecastDTO>>(cancellationToken: ct)
                    ?? new();

                return items.Select(WeatherForecastUiEnricher.Enrich).ToList();
            }
            catch (OperationCanceledException)
            {
                return new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GetLatestWeatherForecastAsync failed: {ex.Message}");
                return new();
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
        public Task<ClientWeatherForecastDTO?> GetById(int id)
            => GetByIdAsync(id, CancellationToken.None);

        public async Task<ClientWeatherForecastDTO?> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                Console.Error.WriteLine("GetByIdAsync: invalid id.");
                return null;
            }

            try
            {
                using var resp = await _http.GetAsync($"WeatherForecast/{id}", ct);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return null;

                resp.EnsureSuccessStatusCode();

                var dto = await resp.Content.ReadFromJsonAsync<ClientWeatherForecastDTO>(cancellationToken: ct);
                return dto is null ? null : WeatherForecastUiEnricher.Enrich(dto);
            }
            catch (OperationCanceledException)
            {
                // request canceled
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetByIdAsync({id}): {ex.Message}");
                return null;
            }
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





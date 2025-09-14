using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Net;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class WeatherForecastService
    {
    #nullable disable
        private readonly HttpClient _httpClient;
        private readonly IHttpClientFactory _httpFactory;
        private const string ClientName = "ApiWithAuth";

        public WeatherForecastService(HttpClient httpClient, IHttpClientFactory httpFactory)
        {
            _httpClient = httpClient;
            _httpFactory = httpFactory;
        }
        private HttpClient Api => _httpFactory.CreateClient(ClientName);

        public async Task AddAsync(WeatherForecastModel WeatherForecastDTO)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("WeatherForecastDTO", WeatherForecastDTO);
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();
                
                var saved = await response.Content.ReadFromJsonAsync<WeatherForecastModel>();
                _ = saved ?? throw new InvalidOperationException("Response content was null");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in AddAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<IEnumerable<WeatherForecastModel?>> GetLatestWeatherForecastAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("WeatherForecast/current");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<WeatherForecastModel>>();
                return list ?? Enumerable.Empty<WeatherForecastModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestWeatherForecastAsync: {ex.Message}");
                throw;
            }
            
        }

        public async Task<List<WeatherForecastModel>> GetLatestForecastNonNullAsync()
        {
            try
            {
                var raw = await GetLatestWeatherForecastAsync();
                if (raw == null) return new List<WeatherForecastModel>();

                var nonNulls = raw.Where(wf => wf != null).ToList();
                return raw.ToNonNullList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestForecastNonNullAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<WeatherForecastModel> SaveWeatherForecastAsync(WeatherForecastModel model)
        {
            try
            {
                var resp = await Api.PostAsJsonAsync("WeatherForecast", model);
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<WeatherForecastModel>()
                       ?? throw new Exception("No data");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SaveWeatherForecastAsync: {ex.Message}");
                throw;
            }
            
        }

        public async Task<WeatherForecastModel> GenerateNewForecastAsync() {
            try
            {
                var generate = await Api.GetFromJsonAsync<WeatherForecastModel>("WeatherForecast/generate");
                if (generate != null)
                {
                    var resp = await Api.PostAsJsonAsync("WeatherForecast", generate);
                    resp.EnsureSuccessStatusCode();
                    return await resp.Content.ReadFromJsonAsync<WeatherForecastModel>()
                           ?? throw new Exception("No data");
                }
                return generate;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GenerateNewForecastAsync: {ex.Message}");
                throw;
            }
        }

        

        public async Task<List<WeatherForecastModel>> GetHistoryAsync(int limit = 128)
        {
            try
            {
                var list = await Api.GetFromJsonAsync<List<WeatherForecastModel>>($"WeatherForecast/history?limit={limit}")
                           ?? new List<WeatherForecastModel>();
                return list;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetHistoryAsync: {ex.Message}");
                throw;
            }
        }
        public async Task<List<WeatherForecastModel>> GetAllAsync(WeatherForecastModel forecast)
        {
            try
            {
                var response = await _httpClient.GetAsync("WeatherForecast/all");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<List<WeatherForecastModel>>();
                return list ?? new List<WeatherForecastModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAllAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task SendWeatherToAllClientsAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync("WeatherForecastDTO/send", null);
                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception("Failed to send weather forecast to all clients");
                }
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SendWeatherToAllClientAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<IReadOnlyList<ClientWeatherForecastDTO>> GetAsync(CancellationToken ct = default)
        {
            try
            {
                // take history (eg: last 50) or just GET WeatherForecast
                var apiList = await Api.GetFromJsonAsync<List<ClientWeatherForecastDTO>>("WeatherForecast/history", ct)
                             ?? new List<ClientWeatherForecastDTO>();
                if (!apiList.Any()) return new List<ClientWeatherForecastDTO>();

                return apiList.Select(Map).ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAsync: {ex.Message}");
                throw;
            }
            
        }

        private static ClientWeatherForecastDTO Map(ClientWeatherForecastDTO x) => new()
        {
            Id = x.Id,
            DateWeather = x.DateWeather,
            TemperatureC = x.TemperatureC,
            // ⚠️ avoids string API property; calculates client-side :
            TemperatureF = (int)Math.Round(32 + x.TemperatureC / 0.5556),
            Summary = x.Summary ?? string.Empty,
            RainfallMm = x.RainfallMm,
            Humidity = x.Humidity,
            WindSpeedKmh = x.WindSpeedKmh,
            Icon = IconFromSummary(x.Summary)
        };

        private static string IconFromSummary(string? s) => s?.ToLowerInvariant() switch
        {
            var t when t.Contains("rain") || t.Contains("pluie") => "wi wi-rain",
            var t when t.Contains("cloud") || t.Contains("nuage") => "wi wi-cloudy",
            var t when t.Contains("sun") || t.Contains("soleil") || t.Contains("clear") => "wi wi-day-sunny",
            _ => "wi wi-na"
        };
    }
}


















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





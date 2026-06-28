using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficConditionService
    {
        private readonly HttpClient _httpClient;
        private string? _eventId;

        public TrafficConditionService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("ApiWithAuth");
        }

        // ✅ Never return null again: always a list
        public async Task<List<ClientTrafficConditionDTO>> GetLatestTrafficConditionAsync(CancellationToken ct = default)
        {
            try
            {
                using var resp = await _httpClient.GetAsync("TrafficCondition/latest", ct);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return new();

                resp.EnsureSuccessStatusCode();

                var list = await resp.Content.ReadFromJsonAsync<List<ClientTrafficConditionDTO>>(cancellationToken: ct);
                return list?.ToNonNullList() ?? new();
            }
            catch (OperationCanceledException)
            {
                return new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestTrafficConditionAsync: {ex.Message}");
                return new();
            }
        }

        public async Task GetCurrent(double lat, double lon)
        {
            // TODO: implementation
        }

        public async Task<ClientTrafficConditionDTO?> GetTrafficConditionByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0)
            {
                Console.Error.WriteLine("GetTrafficConditionByIdAsync: invalid id.");
                return null;
            }

            try
            {
                using var resp = await _httpClient.GetAsync($"TrafficCondition/{id}", ct);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return null;

                resp.EnsureSuccessStatusCode();

                return await resp.Content.ReadFromJsonAsync<ClientTrafficConditionDTO>(cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetTrafficConditionByIdAsync({id}): {ex.Message}");
                return null;
            }
        }

        public async Task<ClientTrafficConditionDTO> SaveTrafficConditionAsync(ClientTrafficConditionDTO dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));

            var resp = await _httpClient.PostAsJsonAsync("TrafficCondition", dto);
            resp.EnsureSuccessStatusCode();

            var saved = await resp.Content.ReadFromJsonAsync<ClientTrafficConditionDTO>();
            return saved ?? throw new InvalidOperationException("Response content was null");
        }

        public async Task<int> ArchivePastTrafficConditionsAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync($"TrafficCondition/archive-expired", content: null);
                if (response.StatusCode == HttpStatusCode.NotFound) return 0;

                response.EnsureSuccessStatusCode();

                var archivedCount = await response.Content.ReadFromJsonAsync<int>();
                return archivedCount;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in ArchivePastTrafficConditionsAsync: {ex.Message}");
                throw;
            }
        }

        // ✅ Wrapper sync consistent with nullable async
        public ClientTrafficConditionDTO? UpdateTrafficCondition(int id, ClientTrafficConditionDTO dto)
            => UpdateTrafficConditionAsync(dto).GetAwaiter().GetResult();

        public async Task<ClientTrafficConditionDTO?> UpdateTrafficConditionAsync(ClientTrafficConditionDTO @traffic)
        {
            try
            {
                var resp = await _httpClient.PutAsJsonAsync("TrafficCondition/update", @traffic);
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;

                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientTrafficConditionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in UpdateTrafficConditionAsync: {ex.Message}");
                return null;
            }
        }

        public async Task<List<ClientTrafficConditionDTO>> GetByCongestionLevelAsync(string congestionLevel, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(congestionLevel))
                return new();

            try
            {
                var url =
                    $"TrafficCondition/by-congestionlevel" +
                    $"?congestionLevel={Uri.EscapeDataString(congestionLevel.Trim())}";

                var list = await _httpClient.GetFromJsonAsync<List<ClientTrafficConditionDTO>>(url, ct);
                return list?.ToNonNullList() ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GetByCongestionLevelAsync failed: {ex.Message}");
                return new();
            }
        }

        public async Task<List<ClientTrafficConditionDTO>> GetByIncidentTypeAsync(string incidentType, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(incidentType))
                return new();

            try
            {
                var url =
                    $"TrafficCondition/by-incidenttype" +
                    $"?incidentType={Uri.EscapeDataString(incidentType.Trim())}";

                var list = await _httpClient.GetFromJsonAsync<List<ClientTrafficConditionDTO>>(url, ct);
                return list?.ToNonNullList() ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GetByIncidentTypeAsync failed: {ex.Message}");
                return new();
            }
        }

        public async Task<List<ClientTrafficConditionDTO>> GetByLocationAsync(string location, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(location))
                return new();

            try
            {
                var url =
                    $"TrafficCondition/by-location" +
                    $"?location={Uri.EscapeDataString(location.Trim())}";

                var list = await _httpClient.GetFromJsonAsync<List<ClientTrafficConditionDTO>>(url, ct);
                return list?.ToNonNullList() ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"GetByLocationAsync failed: {ex.Message}");
                return new();
            }
        }
        private sealed class ArchiveResult
        {
            public int ArchivedCount { get; set; }
        }
    }
}















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





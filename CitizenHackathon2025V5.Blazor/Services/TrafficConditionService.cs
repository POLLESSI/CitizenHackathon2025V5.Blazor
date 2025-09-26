using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using SharedDTOs = CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficConditionService
    {
        private readonly HttpClient _httpClient;
        private const string ApiTrafficConditionBase = "api/TrafficCondition";
        private string? _eventId;

        public TrafficConditionService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("ApiWithAuth"); 
        }

        public async Task<List<ClientTrafficConditionDTO>> GetLatestTrafficConditionAsync()
        {
            try
            {
                var resp = await _httpClient.GetAsync("TrafficCondition/latest");
                if (resp.StatusCode == HttpStatusCode.NotFound) return null; // no exceptions
                resp.EnsureSuccessStatusCode();

                var list = await resp.Content.ReadFromJsonAsync<List<ClientTrafficConditionDTO>>();
                return list?.ToNonNullList() ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestTrafficConditionAsync: {ex.Message}");
                return new List<ClientTrafficConditionDTO>() { new ClientTrafficConditionDTO() };

            }
        }
        public async Task GetCurrent(double lat, double lon)
        {

        }
        public async Task GetTrafficConditionById(int id)
        {

        }

        public async Task<ClientTrafficConditionDTO> SaveTrafficConditionAsync(ClientTrafficConditionDTO dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));

            // Likely "trafficcondition" (check Swagger)
            var resp = await _httpClient.PostAsJsonAsync("TrafficCondition", dto);
            resp.EnsureSuccessStatusCode();

            var saved = await resp.Content.ReadFromJsonAsync<ClientTrafficConditionDTO>();
            return saved ?? throw new InvalidOperationException("Response content was null");
        }
        public async Task<int> ArchivePastTrafficConditionsAsync()
        {
            try
            {
                var response = await _httpClient.PostAsync($"{ApiTrafficConditionBase}/archive-expired", null);
                if (response.StatusCode == HttpStatusCode.NotFound) return 0;
                response.EnsureSuccessStatusCode();

                var archivedCount = await response.Content.ReadFromJsonAsync<int>();
                return archivedCount;
                ;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in ArchivePastTrafficConditionsAsync: {ex.Message}");
                throw;
            }

        }
        public ClientTrafficConditionDTO UpdateTrafficCondition(int id, ClientTrafficConditionDTO dto)
            => UpdateTrafficConditionAsync(dto).GetAwaiter().GetResult();
        public async Task<ClientTrafficConditionDTO?> UpdateTrafficConditionAsync(ClientTrafficConditionDTO @traffic)
        {
            try
            {
                var resp = await _httpClient.PutAsJsonAsync($"{ApiTrafficConditionBase}/update", @traffic);
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
        
        private sealed class ArchiveResult
        {
            public int ArchivedCount { get; set; }
        }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





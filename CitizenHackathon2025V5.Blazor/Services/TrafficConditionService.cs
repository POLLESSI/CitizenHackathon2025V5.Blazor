using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Net;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficConditionService
    {
        private readonly HttpClient _httpClient;

        // ✨ Inject the default client configured in Program.cs (BaseAddress = {api}/api/)
        public TrafficConditionService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("Default"); // BaseAddress = https://localhost:7254/api/
        }

        public async Task<List<TrafficConditionModel>> GetLatestTrafficConditionAsync()
        {
            try
            {
                // ✅ No leading slash, relative to BaseAddress
                //    Adjust the path to match your API controller route shown in Swagger.
                //    Most likely: "trafficcondition/latest"
                // BaseAddress = https://localhost:7254/api/  → do not prefix with "api/
                var resp = await _httpClient.GetAsync("TrafficCondition/latest");
                if (resp.StatusCode == HttpStatusCode.NotFound) return null; // no exceptions
                resp.EnsureSuccessStatusCode();

                var list = await resp.Content.ReadFromJsonAsync<List<TrafficConditionModel>>();
                return list?.ToNonNullList() ?? new();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestTrafficConditionAsync: {ex.Message}");
                throw;
            }
        }

        public async Task<TrafficConditionModel> SaveTrafficConditionAsync(TrafficConditionModel dto)
        {
            if (dto is null) throw new ArgumentNullException(nameof(dto));

            // Likely "trafficcondition" (check Swagger)
            var resp = await _httpClient.PostAsJsonAsync("TrafficCondition", dto);
            resp.EnsureSuccessStatusCode();

            var saved = await resp.Content.ReadFromJsonAsync<TrafficConditionModel>();
            return saved ?? throw new InvalidOperationException("Response content was null");
        }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





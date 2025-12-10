// Client/Services/SuggestionMapService.cs
using System.Net.Http.Json;
using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class SuggestionMapService
    {
        private readonly HttpClient _client;

        public SuggestionMapService(IHttpClientFactory factory)
        {
            _client = factory.CreateClient("ApiWithAuth");
        }

        public async Task<List<SuggestionGroupedByPlaceDTO>> GetSuggestionMapAsync(int days = 7, CancellationToken ct = default)
        {
            try
            {
                var url = $"Suggestions/map?days={days}";
                var result = await _client.GetFromJsonAsync<List<SuggestionGroupedByPlaceDTO>>(url, ct);
                return result ?? new List<SuggestionGroupedByPlaceDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SuggestionMapService] error: {ex.Message}");
                return new List<SuggestionGroupedByPlaceDTO>();
            }
        }
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
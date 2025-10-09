using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using static System.Net.WebRequestMethods;
using SharedDTOs = CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class SuggestionService
    {
    #nullable disable
        private readonly HttpClient _httpClient;
        private string? _suggestionId;

        public SuggestionService(IHttpClientFactory factory)
        {
            _httpClient = factory.CreateClient("ApiWithAuth"); ;
        }
        public async Task<IEnumerable<ClientSuggestionDTO>> GetSuggestionsByUserAsync(int userId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"Suggestions/user/{userId}");
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return Enumerable.Empty<ClientSuggestionDTO>();

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<IEnumerable<ClientSuggestionDTO>>()
                  ?? Enumerable.Empty<ClientSuggestionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetSuggestionByUserAsync: {ex.Message}");
                return Enumerable.Empty<ClientSuggestionDTO>();
            }
            
        }
        public async Task<List<ClientSuggestionDTO>> GetSuggestionsNearbyAsync(double lat, double lng)
        {
            try
            {
                var suggest = await _httpClient.GetFromJsonAsync<List<ClientSuggestionDTO>>(
                    $"Suggestions?lat={lat}&lng={lng}");

                return suggest ?? new List<ClientSuggestionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetSuggestionsNearbyAsync: {ex.Message}");
                return new List<ClientSuggestionDTO>();
            }
        }
        public async Task<ClientSuggestionDTO?> GetById(int id, CancellationToken ct)
        {
            if (id <= 0)
            {
                Console.Error.WriteLine("GetById: invalid id.");
                return null;
            }

            try
            {
                using var resp = await _httpClient.GetAsync($"Suggestions/{id}", ct);

                if (resp.StatusCode == HttpStatusCode.NotFound)
                    return null;

                resp.EnsureSuccessStatusCode();

                return await resp.Content.ReadFromJsonAsync<ClientSuggestionDTO>(cancellationToken: ct);
            }
            catch (OperationCanceledException)
            {
                // Request canceled: we follow the services pattern and return null
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetById({id}): {ex.Message}");
                return null;
            }
        }
        public async Task<bool> SoftDeleteSuggestionAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"Suggestions/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return false;
                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<bool>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SoftDeletedSuggestionAsync: {ex.Message}");
                return false;
            }
            
        }
        public async Task<IEnumerable<ClientSuggestionDTO?>> GetLatestSuggestionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("Suggestions/all");
                if (response.StatusCode == HttpStatusCode.NotFound)
                    return Enumerable.Empty<ClientSuggestionDTO>();

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<IEnumerable<ClientSuggestionDTO>>()
                       ?? Enumerable.Empty<ClientSuggestionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestSuggestionAsync: {ex.Message}");
                return Enumerable.Empty<ClientSuggestionDTO>();
            }
            
        }
        public async Task<ClientSuggestionDTO> SaveSuggestionAsync(ClientSuggestionDTO @suggestion)
        {
            try
            {
                var resp = await _httpClient.PostAsJsonAsync("Suggestions", @suggestion);
                if (resp.StatusCode == HttpStatusCode.NotFound) return null;

                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadFromJsonAsync<ClientSuggestionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SaveSuggestionAsync: {ex.Message}");
                return null;
            }
        }
    }
}























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





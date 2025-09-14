using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using System.Net;
using System.Net.Http.Json;
using static System.Net.WebRequestMethods;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class SuggestionService
    {
#nullable disable
        private readonly HttpClient _httpClient;

        public SuggestionService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<IEnumerable<SuggestionModel>> GetSuggestionsByUserAsync(int userId)
        {
            try
            {
                var response = await _httpClient.GetAsync($"Suggestions/user/{userId}");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<SuggestionModel>>();
                return list ?? Enumerable.Empty<SuggestionModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetSuggestionByUserAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<List<ClientSuggestionDTO>> GetSuggestionsNearbyAsync(double lat, double lng)
        {
            try
            {
                var response = await _httpClient.GetFromJsonAsync<List<ClientSuggestionDTO>>($"Suggestions?lat={lat}&lng={lng}")
                    ?? new List<ClientSuggestionDTO>();
                return response;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetSuggestionNearbyAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<bool> SoftDeleteSuggestionAsync(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"Suggestion/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return false;
                response.EnsureSuccessStatusCode();

                var isDeleted = await response.Content.ReadFromJsonAsync<bool>();
                return isDeleted;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SoftDeletedSuggestionAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<IEnumerable<SuggestionModel?>> GetLatestSuggestionAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("Suggestions/all");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<SuggestionModel>>();
                return list ?? Enumerable.Empty<SuggestionModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetLatestSuggestionAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task<SuggestionModel> SaveSuggestionAsync(SuggestionModel @suggestion)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("Suggestion", @suggestion);
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var saved = await response.Content.ReadFromJsonAsync<SuggestionModel>();
                return saved ?? throw new InvalidOperationException("Response content was null");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in SaveSuggestionAsync: {ex.Message}");
                throw;
            }
            
        }
        //public async SuggestionModel? UpdateSuggestion(SuggestionModel @suggestion)
        //{
        //                var response = await _httpClient.PutAsJsonAsync($"api/suggestions/update", @suggestion);
        //    if (response.IsSuccessStatusCode)
        //    {
        //        return await response.Content.ReadFromJsonAsync<SuggestionModel>();
        //    }
        //    throw new Exception("Failed to update suggestion");
        //    return null; // Placeholder for actual update logic
        //}
    }
}























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





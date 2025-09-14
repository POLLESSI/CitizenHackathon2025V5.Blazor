using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions;
using System.Net;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class GptInteractionService
    {
#nullable disable
        private readonly HttpClient _httpClient;

        public GptInteractionService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<IEnumerable<GptInteractionModel>> GetAllInteractions()
        {
            try
            {
                var response = await _httpClient.GetAsync("Gpt/all");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
                return list ?? Enumerable.Empty<GptInteractionModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAllInteractions: {ex.Message}");
                throw;
            }
            
        }
        public async Task<IEnumerable<GptInteractionModel>> GetById(int id)
        {
            try
            {
                var response = await _httpClient.GetAsync($"Gpt/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return null;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
                return list ?? Enumerable.Empty<GptInteractionModel>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetByIdAsync: {ex.Message}");
                throw;
            }
            
        }
        //public async Task<IEnumerable<GptInteractionModel>> GetSuggestionsByForecastIdAsync(int id)
        //{
        //    var response = await _httpClient.GetAsync($"api/gpt/forecast/{id}");
        //    if (response.IsSuccessStatusCode)
        //    {
        //        return await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
        //    }
        //    return Enumerable.Empty<GptInteractionModel>();
        //}
        //public async Task<IEnumerable<GptInteractionModel>> GetSuggestionsByTrafficIdAsync(int id)
        //{
        //    var response = await _httpClient.GetAsync($"api/gpt/traffic/{id}");
        //    if (response.IsSuccessStatusCode)
        //    {
        //        return await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
        //    }
        //    return Enumerable.Empty<GptInteractionModel>();
        //}
        public async Task AskGpt(GptInteractionModel prompt)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("Gpt/Ask-Gpt", prompt);
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in AskGpt: {ex.Message}");
                throw;
            }
            
        }
        public async Task Delete(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"Gpt/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in DeleteAsync: {ex.Message}");
                throw;
            }
            
        }
        public async Task ReplayInteraction(int id)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"Gpt/Replay/{id}", new { });
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<GptInteractionModel>>();
                return;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in ReplayInteraction: {ex.Message}");
                throw;
            }
            
        }
    }
}























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





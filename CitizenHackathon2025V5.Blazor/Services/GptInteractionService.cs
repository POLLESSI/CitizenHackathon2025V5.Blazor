using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions;
using System.Net;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class GptInteractionService
    {
#nullable disable
        private readonly HttpClient _httpClient;
        //private const string Base = "api/gpt";

        public GptInteractionService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        public async Task<IEnumerable<ClientGptInteractionDTO>> GetAllInteractions()
        {
            try
            {
                var response = await _httpClient.GetAsync("Gpt/all");
                if (response.StatusCode == HttpStatusCode.NotFound) 
                    return Enumerable.Empty<ClientGptInteractionDTO>();
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientGptInteractionDTO>>();
                return list ?? Enumerable.Empty<ClientGptInteractionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAllInteractions: {ex.Message}");
                throw;
            }
            
        }
        public async Task<ClientGptInteractionDTO> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0) return null;

            try
            {
                using var response = await _httpClient.GetAsync($"Gpt/{id}", ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();

                // ⚠️ L’endpoint /api/gpt/{id} devrait renvoyer UN objet, pas une liste
                var item = await response.Content.ReadFromJsonAsync<ClientGptInteractionDTO>(cancellationToken: ct);
                return item;
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetByIdAsync({id}): {ex.Message}");
                return null;
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
        public async Task AskGpt(ClientGptInteractionDTO prompt)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync("Gpt/ask-gpt", prompt);
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientGptInteractionDTO>>();
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

                var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientGptInteractionDTO>>();
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
                var response = await _httpClient.PostAsJsonAsync($"Gpt/replay/{id}", new { });
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();
                
                var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientGptInteractionDTO>>();
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





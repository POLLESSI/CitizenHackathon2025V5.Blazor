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
        private readonly HttpClient _ollamaClient;

        public GptInteractionService(HttpClient httpClient, HttpClient ollamaClient)
        {
            _httpClient = httpClient;
            _ollamaClient = ollamaClient;
        }

        //private const string Base = "api/gpt";
        public async Task<List<ClientGptInteractionDTO>> GetAllInteractions(CancellationToken ct = default)
        {
            try
            {
                using var response = await _httpClient.GetAsync("Gpt/all", ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new List<ClientGptInteractionDTO>();

                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<List<ClientGptInteractionDTO>>(cancellationToken: ct);
                return list ?? new List<ClientGptInteractionDTO>();
            }
            catch (OperationCanceledException)
            {
                return new List<ClientGptInteractionDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAllInteractions: {ex.Message}");
                return new List<ClientGptInteractionDTO>();
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
        public async Task<ClientGptInteractionDTO> AskGpt(ClientGptInteractionDTO prompt)
        {
            try
            {
                // ✅ Use _ollamaClient to avoid the timeout
                var response = await _ollamaClient.PostAsJsonAsync("gpt/ask-mistral", new
                {
                    Prompt = prompt.Prompt,
                    Model = "mistral",
                    MaxTokens = 500 // ✅ Limit the size of the response
                });

                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<ClientGptAnswerDTO>();
                return new ClientGptInteractionDTO
                {
                    Id = result?.Id ?? 0,
                    Prompt = prompt.Prompt,
                    Response = result?.Response ?? "No response from Mistral.",
                    CreatedAt = result?.CreatedAt ?? DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR in AskGpt: {ex.Message}");
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

                //var list = await response.Content.ReadFromJsonAsync<IEnumerable<ClientGptInteractionDTO>>();
                //return;  
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





using CitizenHackathon2025.Blazor.DTOs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class GptInteractionService
    {
#nullable disable
        private readonly HttpClient _httpClient;
        private readonly HttpClient _ollamaClient;

        private const string BaseRoute = "Gpt";

        public GptInteractionService(HttpClient httpClient, IHttpClientFactory httpClientFactory)
        {
            _httpClient = httpClient;
            _ollamaClient = httpClientFactory.CreateClient("OllamaClient");
        }

        public async Task<List<ClientGptInteractionDTO>> GetAllInteractions(CancellationToken ct = default)
        {
            try
            {
                Console.WriteLine($"[GptInteractionService] BaseAddress = {_httpClient.BaseAddress}");

                using var response = await _httpClient.GetAsync($"{BaseRoute}/all", ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new List<ClientGptInteractionDTO>();

                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<List<ClientGptInteractionDTO>>(cancellationToken: ct);
                return list ?? new List<ClientGptInteractionDTO>();
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
                using var response = await _httpClient.GetAsync($"{BaseRoute}/{id}", ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();

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

        public async Task<ClientGptInteractionDTO> AskGpt(ClientGptInteractionDTO prompt)
        {
            try
            {
                var response = await _httpClient.PostAsJsonAsync($"{BaseRoute}/ask-mistral", new
                {
                    Prompt = prompt.Prompt,
                    Latitude = 50.0,
                    Longitude = 4.5
                });

                var raw = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[AskGpt] Status={(int)response.StatusCode} Body={raw}");

                response.EnsureSuccessStatusCode();

                var result = JsonSerializer.Deserialize<ClientGptAnswerDTO>(raw, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return new ClientGptInteractionDTO
                {
                    Id = result?.Id ?? 0,
                    Prompt = result?.Prompt ?? prompt.Prompt,
                    Response = result?.Response ?? "No response from Mistral.",
                    CreatedAt = result?.CreatedAt ?? DateTime.UtcNow,
                    Active = true
                };
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"ERROR in AskGpt: {ex}");
                throw;
            }
        }

        public async Task Delete(int id)
        {
            try
            {
                var response = await _httpClient.DeleteAsync($"{BaseRoute}/{id}");
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();
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
                var response = await _httpClient.PostAsJsonAsync($"{BaseRoute}/replay/{id}", new { });
                if (response.StatusCode == HttpStatusCode.NotFound) return;
                response.EnsureSuccessStatusCode();

                // The endpoint does NOT return IEnumerable<ClientGptInteractionDTO>
                var raw = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[ReplayInteraction] Response = {raw}");
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





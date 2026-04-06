using CitizenHackathon2025.Blazor.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class MessageService
    {
        private readonly HttpClient _http;
        private const string BaseRoute = "Message";

        public MessageService(HttpClient http)
        {
            _http = http;
        }

        public Task<List<ClientMessageDTO>?> GetLatestAsync(int take = 100, CancellationToken ct = default)
            => _http.GetFromJsonAsync<List<ClientMessageDTO>>($"{BaseRoute}/latest?take={take}", ct);

        public async Task<ClientMessageDTO?> PostAsync(string content, CancellationToken ct = default)
        {
            var payload = new { Content = content };
            var resp = await _http.PostAsJsonAsync(BaseRoute, payload, ct);
            var body = await resp.Content.ReadAsStringAsync(ct);

            if (!resp.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"POST {BaseRoute} failed. Status={(int)resp.StatusCode} {resp.ReasonPhrase}. Body={body}");
            }

            if (string.IsNullOrWhiteSpace(body))
                return null;

            return System.Text.Json.JsonSerializer.Deserialize<ClientMessageDTO>(
                body,
                new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
        }

        public async Task<bool> DeleteAsync(int id, CancellationToken ct = default)
        {
            var resp = await _http.DeleteAsync($"{BaseRoute}/{id}", ct);

            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false;

            resp.EnsureSuccessStatusCode();
            return true;
        }
    }
}










































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
using CitizenHackathon2025.Blazor.DTOs;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class MessageService
    {
        private readonly HttpClient _http;

        public MessageService(HttpClient http)
        {
            _http = http;
        }

        public Task<List<ClientMessageDTO>?> GetLatestAsync(int take = 100, CancellationToken ct = default)
            => _http.GetFromJsonAsync<List<ClientMessageDTO>>($"Message/latest?take={take}", ct);

        public async Task<ClientMessageDTO?> PostAsync(string content, CancellationToken ct = default)
        {
            var payload = new { Content = content };
            var resp = await _http.PostAsJsonAsync("Message", payload, ct);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadFromJsonAsync<ClientMessageDTO>(cancellationToken: ct);
        }

    }
}

using CitizenHackathon2025V5.Blazor.Client.Models;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class ProfanityAdminApiService
    {
        private readonly HttpClient _http;
        private const string BaseRoute = "api/Profanity";

        public ProfanityAdminApiService(HttpClient http)
        {
            _http = http;
        }

        public async Task<PagedResultModel<ProfanityWordModel>?> GetPagedAsync(
            int page,
            int pageSize,
            string? languageCode,
            string? search,
            CancellationToken ct = default)
        {
            var query = $"{BaseRoute}?page={page}&pageSize={pageSize}";

            if (!string.IsNullOrWhiteSpace(languageCode))
                query += $"&languageCode={Uri.EscapeDataString(languageCode)}";

            if (!string.IsNullOrWhiteSpace(search))
                query += $"&search={Uri.EscapeDataString(search)}";

            return await _http.GetFromJsonAsync<PagedResultModel<ProfanityWordModel>>(query, ct);
        }

        public async Task<ProfanityWordModel?> CreateAsync(CreateProfanityWordModel request, CancellationToken ct = default)
        {
            var resp = await _http.PostAsJsonAsync(BaseRoute, request, ct);
            resp.EnsureSuccessStatusCode();

            return await resp.Content.ReadFromJsonAsync<ProfanityWordModel>(cancellationToken: ct);
        }

        public async Task SetActiveAsync(int id, bool active, CancellationToken ct = default)
        {
            var resp = await _http.PatchAsync($"{BaseRoute}/{id}/active?active={active}", null, ct);
            resp.EnsureSuccessStatusCode();
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            var resp = await _http.DeleteAsync($"{BaseRoute}/{id}", ct);
            resp.EnsureSuccessStatusCode();
        }
    }
}






















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
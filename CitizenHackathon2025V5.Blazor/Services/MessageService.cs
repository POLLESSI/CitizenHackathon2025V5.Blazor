using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
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

        public async Task<ClientMessageDTO?> PostAsync(CreateMessageRequest request, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            var content = request.Content?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(content))
                return null;

            /*
             * On reconstruit un DTO propre plutôt que de modifier
             * l'objet reçu par le composant.
             */
            var payload = new CreateMessageRequest
            {
                Content = content,

                SourceType = string.IsNullOrWhiteSpace(request.SourceType) ? "Other" : request.SourceType.Trim(),

                RelatedId = request.RelatedId,

                RelatedName = string.IsNullOrWhiteSpace(request.RelatedName) ? null : request.RelatedName.Trim()
            };

            using var response = await _http.PostAsJsonAsync("Message", payload, ct);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadFromJsonAsync<ClientMessageDTO>(cancellationToken: ct);
        }
        public Task<ClientMessageDTO?> PostAsync(string content, CancellationToken ct = default)
        {
            return PostAsync(
                new CreateMessageRequest
                {
                    Content = content?.Trim() ?? string.Empty,

                    SourceType = "Other",
                    RelatedId = null,
                    RelatedName = null
                },
                ct);
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
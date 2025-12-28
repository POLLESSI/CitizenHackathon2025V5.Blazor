using System.Net.Http.Json;
using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class AntennaCrowdService
    {
        private readonly HttpClient _http;
        public AntennaCrowdService(HttpClient http) => _http = http;

        public Task<ClientEventAntennaCrowdDTO?> GetEventCrowdAsync(int eventId, int windowMinutes = 10, double maxRadiusMeters = 5000, CancellationToken ct = default)
            => _http.GetFromJsonAsync<ClientEventAntennaCrowdDTO>(
                $"api/crowdinfoantenna/event/{eventId}/crowd?windowMinutes={windowMinutes}&maxRadiusMeters={maxRadiusMeters}", ct);
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
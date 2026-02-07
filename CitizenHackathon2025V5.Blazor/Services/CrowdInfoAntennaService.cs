using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class CrowdInfoAntennaService : ICrowdInfoAntennaService
    {
        private readonly HttpClient _http;

        public CrowdInfoAntennaService(HttpClient http) => _http = http;

        public async Task<List<ClientCrowdInfoAntennaDTO>?> GetAllAsync(CancellationToken ct = default)
            => await _http.GetFromJsonAsync<List<ClientCrowdInfoAntennaDTO>>("api/crowdinfoantenna", ct);
    }
}










































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
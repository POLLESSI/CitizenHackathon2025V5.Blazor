using System.Net.Http.Json;
using CitizenHackathon2025.Blazor.DTOs.Security;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class AntennaService
    {
        private readonly HttpClient _http;

        public AntennaService(HttpClient http)
        {
            _http = http;
        }

        public async Task<List<ClientCrowdInfoAntennaDTO>> GetAllAsync(CancellationToken ct = default)
        {
            Console.WriteLine($"[AntennaService] BaseAddress = {_http.BaseAddress}");
            var result = await _http.GetFromJsonAsync<List<ClientCrowdInfoAntennaDTO>>(
                "crowdinfoantenna",
                cancellationToken: ct);

            return result ?? new List<ClientCrowdInfoAntennaDTO>();
        }
    }
}































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
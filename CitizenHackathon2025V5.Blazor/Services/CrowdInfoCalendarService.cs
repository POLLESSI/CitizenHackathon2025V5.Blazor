using CitizenHackathon2025.Blazor.DTOs;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class CrowdInfoCalendarService
    {
        private readonly HttpClient _httpClient;
        private readonly JsonSerializerOptions _json;

        private const string BaseRoute = "crowd/calendar";
        private const string AdvisoriesRoute = "crowd/advisories";

        public CrowdInfoCalendarService(HttpClient http)
        {
            _httpClient = http;
            _json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        }

        public Task<List<ClientCrowdInfoCalendarDTO>?> GetAllAsync(CancellationToken ct = default) =>
            _httpClient.GetFromJsonAsync<List<ClientCrowdInfoCalendarDTO>>($"{BaseRoute}/all", _json, ct);

        public async Task<List<ClientCrowdInfoCalendarDTO>> GetAllSafeAsync(CancellationToken ct = default)
        {
            try
            {
                Console.WriteLine($"[CrowdInfoCalendarService] BaseAddress = {_httpClient.BaseAddress}");

                using var response = await _httpClient.GetAsync($"{BaseRoute}/all", ct);

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new List<ClientCrowdInfoCalendarDTO>();

                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<List<ClientCrowdInfoCalendarDTO>>(cancellationToken: ct);
                return list ?? new List<ClientCrowdInfoCalendarDTO>();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in GetAllSafeAsync: {ex.Message}");
                return new List<ClientCrowdInfoCalendarDTO>();
            }
        }

        public async Task<List<ClientCrowdInfoCalendarDTO>?> ListAsync(
            DateTime? from = null,
            DateTime? to = null,
            string? region = null,
            int? placeId = null,
            bool? active = true,
            CancellationToken ct = default)
        {
            var qs = new List<string>();
            if (from is not null) qs.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
            if (to is not null) qs.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
            if (!string.IsNullOrWhiteSpace(region)) qs.Add($"region={Uri.EscapeDataString(region)}");
            if (placeId is not null) qs.Add($"placeId={placeId.Value}");
            if (active is not null) qs.Add($"active={(active.Value ? "true" : "false")}");

            var url = $"{BaseRoute}" + (qs.Count > 0 ? "?" + string.Join("&", qs) : string.Empty);
            return await _httpClient.GetFromJsonAsync<List<ClientCrowdInfoCalendarDTO>>(url, _json, ct);
        }

        public Task<ClientCrowdInfoCalendarDTO?> GetByIdAsync(int id, CancellationToken ct = default) =>
            _httpClient.GetFromJsonAsync<ClientCrowdInfoCalendarDTO>($"{BaseRoute}/{id}", _json, ct);

        public Task<List<string>?> GetAdvisoriesAsync(string region, int? placeId = null, CancellationToken ct = default)
        {
            var url = $"{AdvisoriesRoute}?region={Uri.EscapeDataString(region)}";
            if (placeId is not null) url += $"&placeId={placeId.Value}";
            return _httpClient.GetFromJsonAsync<List<string>>(url, _json, ct);
        }

        public async Task<ClientCrowdInfoCalendarDTO?> CreateAsync(ClientCrowdInfoCalendarDTO dto, CancellationToken ct = default)
        {
            NormalizeTimes(dto);
            var res = await _httpClient.PostAsJsonAsync(BaseRoute, dto, _json, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "create");

            return await res.Content.ReadFromJsonAsync<ClientCrowdInfoCalendarDTO>(_json, ct);
        }

        public async Task UpdateAsync(int id, ClientCrowdInfoCalendarDTO dto, CancellationToken ct = default)
        {
            NormalizeTimes(dto);
            var res = await _httpClient.PutAsJsonAsync($"{BaseRoute}/{id}", dto, _json, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "update");
        }

        public async Task<ClientCrowdInfoCalendarDTO?> UpsertAsync(ClientCrowdInfoCalendarDTO dto, CancellationToken ct = default)
        {
            NormalizeTimes(dto);
            var res = await _httpClient.PostAsJsonAsync($"{BaseRoute}/upsert", dto, _json, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "upsert");

            return await res.Content.ReadFromJsonAsync<ClientCrowdInfoCalendarDTO>(_json, ct);
        }

        public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
        {
            var res = await _httpClient.DeleteAsync($"{BaseRoute}/{id}", ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "soft-delete");
        }

        public async Task RestoreAsync(int id, CancellationToken ct = default)
        {
            var res = await _httpClient.PostAsync($"{BaseRoute}/{id}/restore", content: null, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "restore");
        }

        public async Task HardDeleteAsync(int id, CancellationToken ct = default)
        {
            var res = await _httpClient.DeleteAsync($"{BaseRoute}/{id}/hard", ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "hard-delete");
        }

        private static void NormalizeTimes(ClientCrowdInfoCalendarDTO dto)
        {
            if (dto.StartLocalTime is TimeSpan s && s.Seconds == 0)
                dto.StartLocalTime = new TimeSpan(s.Hours, s.Minutes, 0);

            if (dto.EndLocalTime is TimeSpan e && e.Seconds == 0)
                dto.EndLocalTime = new TimeSpan(e.Hours, e.Minutes, 0);
        }

        private static async Task<Exception> CreateHttpError(HttpResponseMessage res, string action)
        {
            var body = await res.Content.ReadAsStringAsync();
            return new HttpRequestException(
                $"CrowdCalendar {action} failed: {(int)res.StatusCode} {res.ReasonPhrase}. Body: {body}");
        }
    }
}































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
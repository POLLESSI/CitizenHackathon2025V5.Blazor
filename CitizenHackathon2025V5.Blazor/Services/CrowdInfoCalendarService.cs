using System.Net.Http.Json;
using System.Text.Json;
using CitizenHackathon2025V5.Blazor.Client.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    /// <summary>
    /// Client service to call the CrowdCalendar API (CRUD + upsert + advisories).
    /// Uses the unique ClientCrowdInfoCalendarDTO contract (API mirror).
    /// </summary>
    public class CrowdInfoCalendarService
    {
        private readonly HttpClient _http;
        private readonly JsonSerializerOptions _json;

        // Adapt if your API has a different prefix
        private const string BaseRoute = "api/crowd/calendar";
        private const string AdvisoriesRoute = "api/crowd/advisories";

        public CrowdInfoCalendarService(HttpClient http)
        {
            _http = http;
            _json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
            // .NET 8 can (de)serialize TimeSpan natively to "HH:mm:ss"
            // If your server strictly requires HH:mm:ss, keep xx:xx:00 values ​​on the UI side.
        }

        // ---- READ ----

        /// <summary>GET api/crowd/calendar/all (returns everything, even inactive ones if the server allows it)</summary>
        public Task<List<ClientCrowdInfoCalendarDTO>?> GetAllAsync(CancellationToken ct = default) =>
            _http.GetFromJsonAsync<List<ClientCrowdInfoCalendarDTO>>($"{BaseRoute}/all", _json, ct);

        /// <summary>GET api/crowd/calendar?from=..&to=..&region=..&placeId=..&active=..</summary>
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
            return await _http.GetFromJsonAsync<List<ClientCrowdInfoCalendarDTO>>(url, _json, ct);
        }

        /// <summary>GET api/crowd/calendar/{id}</summary>
        public Task<ClientCrowdInfoCalendarDTO?> GetByIdAsync(int id, CancellationToken ct = default) =>
            _http.GetFromJsonAsync<ClientCrowdInfoCalendarDTO>($"{BaseRoute}/{id}", _json, ct);

        /// <summary>GET api/crowd/advisories?region=...&placeId=...</summary>
        public Task<List<string>?> GetAdvisoriesAsync(string region, int? placeId = null, CancellationToken ct = default)
        {
            var url = $"{AdvisoriesRoute}?region={Uri.EscapeDataString(region)}";
            if (placeId is not null) url += $"&placeId={placeId.Value}";
            return _http.GetFromJsonAsync<List<string>>(url, _json, ct);
        }

        // ---- CREATE / UPDATE / UPSERT ----

        /// <summary>POST api/crowd/calendar</summary>
        public async Task<ClientCrowdInfoCalendarDTO?> CreateAsync(ClientCrowdInfoCalendarDTO dto, CancellationToken ct = default)
        {
            NormalizeTimes(dto);
            var res = await _http.PostAsJsonAsync(BaseRoute, dto, _json, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "create");

            // the API returns CreatedAtAction => reloads the element
            return await res.Content.ReadFromJsonAsync<ClientCrowdInfoCalendarDTO>(_json, ct);
        }

        /// <summary>PUT api/crowd/calendar/{id}</summary>
        public async Task UpdateAsync(int id, ClientCrowdInfoCalendarDTO dto, CancellationToken ct = default)
        {
            NormalizeTimes(dto);
            var res = await _http.PutAsJsonAsync($"{BaseRoute}/{id}", dto, _json, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "update");
        }

        /// <summary>POST api/crowd/calendar/upsert</summary>
        public async Task<ClientCrowdInfoCalendarDTO?> UpsertAsync(ClientCrowdInfoCalendarDTO dto, CancellationToken ct = default)
        {
            NormalizeTimes(dto);
            var res = await _http.PostAsJsonAsync($"{BaseRoute}/upsert", dto, _json, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "upsert");

            return await res.Content.ReadFromJsonAsync<ClientCrowdInfoCalendarDTO>(_json, ct);
        }

        // ---- DELETE / RESTORE ----

        /// <summary>DELETE api/crowd/calendar/{id} (soft delete)</summary>
        public async Task SoftDeleteAsync(int id, CancellationToken ct = default)
        {
            var res = await _http.DeleteAsync($"{BaseRoute}/{id}", ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "soft-delete");
        }

        /// <summary>POST api/crowd/calendar/{id}/restore</summary>
        public async Task RestoreAsync(int id, CancellationToken ct = default)
        {
            var res = await _http.PostAsync($"{BaseRoute}/{id}/restore", content: null, ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "restore");
        }

        /// <summary>DELETE api/crowd/calendar/{id}/hard</summary>
        public async Task HardDeleteAsync(int id, CancellationToken ct = default)
        {
            var res = await _http.DeleteAsync($"{BaseRoute}/{id}/hard", ct);
            if (!res.IsSuccessStatusCode)
                throw await CreateHttpError(res, "hard-delete");
        }

        // ---- Helpers ----

        /// <summary>
        /// The backend expects TimeSpan in HH:mm:ss format.
        /// This normalization avoids 400 if you arrive with "06:30".
        /// </summary>
        private static void NormalizeTimes(ClientCrowdInfoCalendarDTO dto)
        {
            // nothing to do if null; System.Text.Json will return null.
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
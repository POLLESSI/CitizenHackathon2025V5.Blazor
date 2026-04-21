using CitizenHackathon2025.Blazor.DTOs;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class GptInteractionService
    {
#nullable disable
        private readonly HttpClient _httpClient;

        private const string BaseRoute = "Gpt";
        private const int MaxLoggedBodyLength = 1200;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        public GptInteractionService(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<List<ClientGptInteractionDTO>> GetAllInteractions(CancellationToken ct = default)
        {
            var url = $"{BaseRoute}/all";

            try
            {
                LogInfo($"[GetAllInteractions] GET {BuildAbsoluteUrl(url)}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                LogInfo($"[GetAllInteractions] HTTP {(int)response.StatusCode} {response.StatusCode}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return new List<ClientGptInteractionDTO>();

                response.EnsureSuccessStatusCode();

                var list = await response.Content.ReadFromJsonAsync<List<ClientGptInteractionDTO>>(JsonOptions, ct);
                return list ?? new List<ClientGptInteractionDTO>();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn("[GetAllInteractions] Cancelled by caller.");
                return new List<ClientGptInteractionDTO>();
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[GetAllInteractions] Timed out or cancelled by HttpClient. {ex.Message}");
                return new List<ClientGptInteractionDTO>();
            }
            catch (Exception ex)
            {
                LogError($"[GetAllInteractions] Unexpected error: {ex}");
                return new List<ClientGptInteractionDTO>();
            }
        }

        public async Task<ClientGptInteractionDTO> GetByIdAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0)
                return null;

            var url = $"{BaseRoute}/{id}";

            try
            {
                LogInfo($"[GetByIdAsync] GET {BuildAbsoluteUrl(url)}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                LogInfo($"[GetByIdAsync] HTTP {(int)response.StatusCode} {response.StatusCode} for id={id}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<ClientGptInteractionDTO>(JsonOptions, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn($"[GetByIdAsync] Cancelled by caller for id={id}.");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[GetByIdAsync] Timed out or cancelled by HttpClient for id={id}. {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"[GetByIdAsync] Unexpected error for id={id}: {ex}");
                return null;
            }
        }

        public async Task<ClientGptInteractionDTO> AskGptSync(
            string prompt,
            double? latitude = null,
            double? longitude = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return null;

            var payload = new AskGptRequest
            {
                Prompt = prompt.Trim(),
                Latitude = latitude,
                Longitude = longitude
            };

            var url = $"{BaseRoute}/ask-mistral-sync";

            try
            {
                LogInfo($"[AskGptSync] POST {BuildAbsoluteUrl(url)} | promptLength={payload.Prompt.Length} | lat={latitude?.ToString() ?? "null"} | lng={longitude?.ToString() ?? "null"}");

                using var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                LogInfo($"[AskGptSync] HTTP {(int)response.StatusCode} {response.StatusCode} | body={TruncateForLog(raw)}");

                response.EnsureSuccessStatusCode();

                var interaction = JsonSerializer.Deserialize<ClientGptInteractionDTO>(raw, JsonOptions);

                if (interaction is not null && interaction.Id > 0)
                {
                    interaction.Prompt ??= payload.Prompt;
                    interaction.Response ??= string.Empty;
                    interaction.Active = true;

                    if (interaction.CreatedAt == default)
                        interaction.CreatedAt = DateTime.UtcNow;

                    LogInfo($"[AskGptSync] Parsed interaction successfully. id={interaction.Id}, hasResponse={!string.IsNullOrWhiteSpace(interaction.Response)}");
                    return interaction;
                }

                throw new InvalidOperationException("Unexpected response format returned by api/Gpt/ask-mistral-sync.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn("[AskGptSync] Cancelled by caller.");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[AskGptSync] Timed out or cancelled by HttpClient. {ex.Message}");
                throw;
            }
            catch (HttpRequestException ex)
            {
                LogError($"[AskGptSync] HTTP error: {ex}");
                throw;
            }
            catch (JsonException ex)
            {
                LogError($"[AskGptSync] JSON parse error: {ex}");
                throw;
            }
            catch (Exception ex)
            {
                LogError($"[AskGptSync] Unexpected error: {ex}");
                throw;
            }
        }

        public async Task<ClientGptStartResponseDTO> StartGptAsync(
            string prompt,
            double? latitude = null,
            double? longitude = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return null;

            var payload = new AskGptRequest
            {
                Prompt = prompt.Trim(),
                Latitude = latitude,
                Longitude = longitude
            };

            var url = $"{BaseRoute}/ask-mistral";

            try
            {
                LogInfo($"[StartGptAsync] POST {BuildAbsoluteUrl(url)} | promptLength={payload.Prompt.Length} | lat={latitude?.ToString() ?? "null"} | lng={longitude?.ToString() ?? "null"}");

                using var response = await _httpClient.PostAsJsonAsync(url, payload, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                LogInfo($"[StartGptAsync] HTTP {(int)response.StatusCode} {response.StatusCode} | body={TruncateForLog(raw)}");

                response.EnsureSuccessStatusCode();

                var started = JsonSerializer.Deserialize<ClientGptStartResponseDTO>(raw, JsonOptions);

                if (started is not null && started.InteractionId > 0)
                    return started;

                throw new InvalidOperationException("Unexpected response format returned by api/Gpt/ask-mistral.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn("[StartGptAsync] Cancelled by caller.");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[StartGptAsync] Timed out or cancelled by HttpClient. {ex.Message}");
                throw;
            }
            catch (HttpRequestException ex)
            {
                LogError($"[StartGptAsync] HTTP error: {ex}");
                throw;
            }
            catch (JsonException ex)
            {
                LogError($"[StartGptAsync] JSON parse error: {ex}");
                throw;
            }
            catch (Exception ex)
            {
                LogError($"[StartGptAsync] Unexpected error: {ex}");
                throw;
            }
        }

        public async Task<ClientGptStatusResponseDTO> GetStatusAsync(int interactionId, CancellationToken ct = default)
        {
            if (interactionId <= 0)
                return null;

            var url = $"{BaseRoute}/status/{interactionId}";

            try
            {
                LogInfo($"[GetStatusAsync] GET {BuildAbsoluteUrl(url)}");

                using var request = new HttpRequestMessage(HttpMethod.Get, url);
                using var response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    ct);

                LogInfo($"[GetStatusAsync] HTTP {(int)response.StatusCode} {response.StatusCode} for interactionId={interactionId}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return null;

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadFromJsonAsync<ClientGptStatusResponseDTO>(JsonOptions, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn($"[GetStatusAsync] Cancelled by caller for interactionId={interactionId}.");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[GetStatusAsync] Timed out or cancelled by HttpClient for interactionId={interactionId}. {ex.Message}");
                return null;
            }
            catch (Exception ex)
            {
                LogError($"[GetStatusAsync] Unexpected error for interactionId={interactionId}: {ex}");
                return null;
            }
        }

        public async Task DeleteAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0)
                return;

            var url = $"{BaseRoute}/{id}";

            try
            {
                LogInfo($"[DeleteAsync] DELETE {BuildAbsoluteUrl(url)}");

                using var response = await _httpClient.DeleteAsync(url, ct);

                LogInfo($"[DeleteAsync] HTTP {(int)response.StatusCode} {response.StatusCode} for id={id}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;

                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn($"[DeleteAsync] Cancelled by caller for id={id}.");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[DeleteAsync] Timed out or cancelled by HttpClient for id={id}. {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                LogError($"[DeleteAsync] Unexpected error for id={id}: {ex}");
                throw;
            }
        }

        public async Task CancelGptRequestAsync(int interactionId, string requestId, CancellationToken ct = default)
        {
            if (interactionId <= 0)
                throw new ArgumentOutOfRangeException(nameof(interactionId));

            var url = $"{BaseRoute}/cancel/{interactionId}";

            if (!string.IsNullOrWhiteSpace(requestId))
                url += $"?requestId={Uri.EscapeDataString(requestId)}";

            try
            {
                LogInfo($"[CancelGptRequestAsync] POST {BuildAbsoluteUrl(url)}");

                using var response = await _httpClient.PostAsync(url, content: null, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                LogInfo($"[CancelGptRequestAsync] HTTP {(int)response.StatusCode} {response.StatusCode} | body={TruncateForLog(raw)}");

                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn($"[CancelGptRequestAsync] Cancelled by caller for interactionId={interactionId}.");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[CancelGptRequestAsync] Timed out or cancelled by HttpClient for interactionId={interactionId}. {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                LogError($"[CancelGptRequestAsync] Unexpected error for interactionId={interactionId}: {ex}");
                throw;
            }
        }

        public async Task ReplayInteractionAsync(int id, CancellationToken ct = default)
        {
            if (id <= 0)
                return;

            var url = $"{BaseRoute}/replay/{id}";

            try
            {
                LogInfo($"[ReplayInteractionAsync] POST {BuildAbsoluteUrl(url)}");

                using var response = await _httpClient.PostAsJsonAsync(url, new { }, ct);
                var raw = await response.Content.ReadAsStringAsync(ct);

                LogInfo($"[ReplayInteractionAsync] HTTP {(int)response.StatusCode} {response.StatusCode} | body={TruncateForLog(raw)}");

                if (response.StatusCode == HttpStatusCode.NotFound)
                    return;

                response.EnsureSuccessStatusCode();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                LogWarn($"[ReplayInteractionAsync] Cancelled by caller for id={id}.");
                throw;
            }
            catch (TaskCanceledException ex)
            {
                LogWarn($"[ReplayInteractionAsync] Timed out or cancelled by HttpClient for id={id}. {ex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                LogError($"[ReplayInteractionAsync] Unexpected error for id={id}: {ex}");
                throw;
            }
        }

        public Task Delete(int id) => DeleteAsync(id);
        public Task ReplayInteraction(int id) => ReplayInteractionAsync(id);

        private string BuildAbsoluteUrl(string relativeOrAbsolute)
        {
            try
            {
                if (Uri.TryCreate(relativeOrAbsolute, UriKind.Absolute, out var absolute))
                    return absolute.ToString();

                if (_httpClient.BaseAddress is null)
                    return relativeOrAbsolute;

                return new Uri(_httpClient.BaseAddress, relativeOrAbsolute).ToString();
            }
            catch
            {
                return relativeOrAbsolute;
            }
        }

        private static string TruncateForLog(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "<empty>";

            var sanitized = value.Replace(Environment.NewLine, " ")
                                 .Replace("\n", " ")
                                 .Replace("\r", " ")
                                 .Trim();

            if (sanitized.Length <= MaxLoggedBodyLength)
                return sanitized;

            return sanitized[..MaxLoggedBodyLength] + "... [truncated]";
        }

        private static void LogInfo(string message) => Console.WriteLine($"[GptInteractionService] {message}");
        private static void LogWarn(string message) => Console.WriteLine($"[GptInteractionService][WARN] {message}");
        private static void LogError(string message) => Console.Error.WriteLine($"[GptInteractionService][ERROR] {message}");

        private sealed class AskGptRequest
        {
            public string Prompt { get; set; } = string.Empty;
            public double? Latitude { get; set; }
            public double? Longitude { get; set; }
        }

        public sealed class ClientGptStatusResponseDTO
        {
            public int Id { get; set; }
            public bool IsCompleted { get; set; }
            public string Response { get; set; }
            public DateTime CreatedAt { get; set; }

            public string Message =>
                IsCompleted
                    ? "Generation completed."
                    : "Generation still running.";
        }
    }
}






















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





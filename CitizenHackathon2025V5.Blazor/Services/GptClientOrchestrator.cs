using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using System.Collections.Concurrent;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class GptClientOrchestrator : IGptClientOrchestrator, IAsyncDisposable
    {
        private readonly GptInteractionService _gptService;
        private readonly IMultiHubSignalRClient _multiHub;

        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly ConcurrentDictionary<int, LiveInteractionState> _live = new();
        private readonly ConcurrentDictionary<int, byte> _finalRaised = new();
        private readonly List<IDisposable> _hubSubscriptions = new();

        private bool _disposed;
        private bool _handlersRegistered;

        private int? _currentInteractionId;
        private string? _currentRequestId;

        private const int PollingInitialDelayMs = 5000;
        private const int PollingIntervalMs = 5000;
        private const int PollingMaxAttempts = 72; // 72 * 5s = 6 minutes
        private static readonly bool EnableVerboseGptPollingLogs = false;

        public GptClientOrchestrator(GptInteractionService gptService, IMultiHubSignalRClient multiHub)
        {
            _gptService = gptService;
            _multiHub = multiHub;
        }

        public event Func<ClientGptInteractionDTO, Task>? InteractionUpdated;
        public event Func<ClientGptInteractionDTO, Task>? InteractionCompleted;
        public event Func<string, Task>? StatusChanged;

        public bool EnablePollingFallback { get; set; } = true;

        public bool IsHubConnected => _multiHub.IsConnected(HubName.Gpt);

        public bool HasReceivedHubEvent { get; private set; }

        public async Task EnsureHubAsync(CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (IsHubConnected && _handlersRegistered)
                return;

            await _connectionLock.WaitAsync(ct);

            try
            {
                if (!IsHubConnected)
                {
                    Console.WriteLine("[GptClientOrchestrator] Connecting GPT hub...");
                    await _multiHub.ConnectAsync(HubName.Gpt, ct);
                    Console.WriteLine("[GptClientOrchestrator] GPT hub connected.");
                }

                if (!_handlersRegistered)
                {
                    RegisterHubHandlers();
                    _handlersRegistered = true;
                    Console.WriteLine("[GptClientOrchestrator] GPT hub handlers registered.");
                }
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task<ClientGptStartResponseDTO?> StartAsync(string prompt, double? latitude = null, double? longitude = null, string languageCode = "fr-FR", CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(prompt))
                return null;

            await EnsureHubAsync(ct);

            var started = await _gptService.StartGptAsync(
                prompt: prompt,
                latitude: latitude,
                longitude: longitude,
                languageCode: languageCode,
                ct: ct);

            if (started is null || started.InteractionId <= 0)
                return null;

            _currentInteractionId = started.InteractionId;
            _currentRequestId = started.RequestId;

            var live = _live.GetOrAdd(
                started.InteractionId,
                _ => new LiveInteractionState(started.InteractionId));

            live.RequestId = started.RequestId;
            live.IsCompleted = false;
            live.HasReceivedHubEvent = false;
            live.ResponseBuffer.Clear();

            live.Interaction = new ClientGptInteractionDTO
            {
                Id = started.InteractionId,
                Prompt = prompt.Trim(),
                Response = string.Empty,
                CreatedAt = started.StartedAtUtc == default
                    ? DateTime.UtcNow
                    : started.StartedAtUtc,
                Active = true,
                Latitude = latitude,
                Longitude = longitude,
                SourceType = "MistralLocal"
            };

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = started.InteractionId,
                IsCompleted = false,
                CreatedAt = started.StartedAtUtc == default
                    ? DateTime.UtcNow
                    : started.StartedAtUtc,
                Status = started.Status ?? "accepted",
                Message = started.Message ?? "Request accepted."
            };

            await RaiseInteractionUpdatedAsync(CloneInteraction(live.Interaction));
            await RaiseStatusChangedAsync(started.Message ?? "Request accepted.");

            if (EnablePollingFallback)
                StartPollingFallback(started.InteractionId, started.RequestId);

            return started;
        }

        public async Task<bool> CancelCurrentAsync(CancellationToken ct = default)
        {
            if (!_currentInteractionId.HasValue || _currentInteractionId.Value <= 0)
                return false;

            return await CancelAsync(
                _currentInteractionId.Value,
                _currentRequestId,
                ct);
        }

        public async Task<bool> CancelAsync(int interactionId, string? requestId = null, CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (interactionId <= 0)
                return false;

            try
            {
                await _gptService.CancelGptRequestAsync(
                    interactionId,
                    requestId ?? string.Empty,
                    ct);

                if (_live.TryGetValue(interactionId, out var live))
                {
                    live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
                    {
                        Id = interactionId,
                        IsCompleted = false,
                        CreatedAt = DateTime.UtcNow,
                        Status = "cancelled",
                        Message = "Generation cancelled."
                    };
                }

                await RaiseStatusChangedAsync("Generation cancelled.");

                if (_currentInteractionId == interactionId)
                {
                    _currentInteractionId = null;
                    _currentRequestId = null;
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[GptClientOrchestrator] CancelAsync failed for interactionId={interactionId}: {ex}");

                return false;
            }
        }

        public bool TryGetLiveInteraction(int interactionId, out ClientGptInteractionDTO? interaction)
        {
            interaction = null;

            if (_live.TryGetValue(interactionId, out var live) &&
                live.Interaction is not null)
            {
                interaction = CloneInteraction(live.Interaction);
                return true;
            }

            return false;
        }

        public IReadOnlyCollection<ClientGptInteractionDTO> GetLiveInteractionsSnapshot()
        {
            return _live.Values
                .Where(x => x.Interaction is not null)
                .Select(x => CloneInteraction(x.Interaction!))
                .OrderByDescending(x => x.CreatedAt)
                .ToList();
        }

        public async Task DisconnectAsync()
        {
            if (_disposed)
                return;

            foreach (var sub in _hubSubscriptions)
            {
                try { sub.Dispose(); }
                catch { }
            }

            _hubSubscriptions.Clear();
            _handlersRegistered = false;

            try
            {
                await _multiHub.DisconnectAsync(HubName.Gpt);
            }
            catch
            {
            }
        }

        private void RegisterHubHandlers()
        {
            _hubSubscriptions.Add(
                _multiHub.RegisterHandler<ClientGptResponseStartedDTO>(
                    HubName.Gpt,
                    nameof(IGptClient.ReceiveGptResponseStarted),
                    dto => HandleStartedAsync(dto)));

            _hubSubscriptions.Add(
                _multiHub.RegisterHandler<ClientGptResponseChunkDTO>(
                    HubName.Gpt,
                    nameof(IGptClient.ReceiveGptResponseChunk),
                    dto => HandleChunkAsync(dto)));

            _hubSubscriptions.Add(
                _multiHub.RegisterHandler<ClientGptResponseStatusDTO>(
                    HubName.Gpt,
                    nameof(IGptClient.ReceiveGptResponseStatus),
                    dto => HandleStatusAsync(dto)));

            _hubSubscriptions.Add(
                _multiHub.RegisterHandler<ClientGptInteractionCompletedDTO>(
                    HubName.Gpt,
                    nameof(IGptClient.ReceiveGptResponseCompleted),
                    dto => HandleCompletedAsync(dto)));
        }

        private async Task HandleStartedAsync(ClientGptResponseStartedDTO dto)
        {
            if (dto.InteractionId <= 0)
                return;

            Console.WriteLine(
                $"[GptClientOrchestrator] STARTED received -> InteractionId={dto.InteractionId}, RequestId={dto.RequestId}");

            HasReceivedHubEvent = true;

            var live = _live.GetOrAdd(
                dto.InteractionId,
                _ => new LiveInteractionState(dto.InteractionId));

            live.HasReceivedHubEvent = true;
            live.RequestId = dto.RequestId;

            live.Interaction ??= new ClientGptInteractionDTO
            {
                Id = dto.InteractionId,
                Prompt = string.Empty,
                Response = string.Empty,
                CreatedAt = dto.StartedAtUtc == default
                    ? DateTime.UtcNow
                    : dto.StartedAtUtc,
                Active = true,
                SourceType = "MistralLocal"
            };

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = dto.InteractionId,
                IsCompleted = false,
                CreatedAt = dto.StartedAtUtc == default
                    ? DateTime.UtcNow
                    : dto.StartedAtUtc,
                Status = "started",
                Message = "Generation started."
            };

            await RaiseInteractionUpdatedAsync(CloneInteraction(live.Interaction));
            await RaiseStatusChangedAsync("Generation started.");
        }

        private async Task HandleChunkAsync(ClientGptResponseChunkDTO dto)
        {
            if (dto.InteractionId <= 0)
                return;

            Console.WriteLine(
                $"[GptClientOrchestrator] CHUNK received -> InteractionId={dto.InteractionId}, ChunkLength={dto.Chunk?.Length ?? 0}, IsFinal={dto.IsFinal}");

            HasReceivedHubEvent = true;

            var live = _live.GetOrAdd(
                dto.InteractionId,
                _ => new LiveInteractionState(dto.InteractionId));

            live.HasReceivedHubEvent = true;
            live.RequestId ??= dto.RequestId;

            live.Interaction ??= new ClientGptInteractionDTO
            {
                Id = dto.InteractionId,
                Prompt = string.Empty,
                Response = string.Empty,
                CreatedAt = DateTime.UtcNow,
                Active = true,
                SourceType = "MistralLocal"
            };

            if (!string.IsNullOrEmpty(dto.Chunk))
                live.ResponseBuffer.Append(dto.Chunk);

            live.Interaction.Response = live.ResponseBuffer.ToString();

            await RaiseInteractionUpdatedAsync(CloneInteraction(live.Interaction));

            if (dto.IsFinal)
            {
                Console.WriteLine(
                    $"[GPT CHUNK] Final chunk received for {dto.InteractionId}, waiting for COMPLETED event.");
            }
        }

        private async Task HandleStatusAsync(ClientGptResponseStatusDTO dto)
        {
            if (dto.InteractionId <= 0)
                return;

            if (EnableVerboseGptPollingLogs || dto.IsTerminal)
            {
                Console.WriteLine(
                    $"[GptClientOrchestrator] STATUS received -> InteractionId={dto.InteractionId}, Status={dto.Status}, IsTerminal={dto.IsTerminal}");
            }

            var live = _live.GetOrAdd(
                dto.InteractionId,
                _ => new LiveInteractionState(dto.InteractionId));

            live.HasReceivedHubEvent = true;
            live.RequestId ??= dto.RequestId;

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = dto.InteractionId,
                IsCompleted =
                    dto.IsTerminal &&
                    string.Equals(dto.Status, "completed", StringComparison.OrdinalIgnoreCase),
                CreatedAt = dto.TimestampUtc == default
                    ? DateTime.UtcNow
                    : dto.TimestampUtc,
                Status = dto.Status,
                Message = dto.Message
            };

            if (!string.IsNullOrWhiteSpace(dto.Message))
                await RaiseStatusChangedAsync(dto.Message);
            else if (!string.IsNullOrWhiteSpace(dto.Status))
                await RaiseStatusChangedAsync(dto.Status);
        }

        private async Task HandleCompletedAsync(ClientGptInteractionCompletedDTO dto)
        {
            if (dto.Id <= 0)
                return;

            Console.WriteLine($"[GPT CLIENT] COMPLETED {dto.Id}");
            Console.WriteLine(
                $"[GptClientOrchestrator] COMPLETED received -> InteractionId={dto.Id}");

            HasReceivedHubEvent = true;

            var completed = MapCompletedDto(dto);

            await CompleteInteractionAsync(completed, source: "SignalR");
        }

        private void StartPollingFallback(int interactionId, string? requestId)
        {
            _ = Task.Run(
                async () =>
                {
                    try
                    {
                        await PollFallbackAsync(
                            interactionId,
                            requestId,
                            CancellationToken.None);
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine(
                            $"[GptClientOrchestrator] Polling fallback task failed for interactionId={interactionId}: {ex}");
                    }
                },
                CancellationToken.None);
        }

        private async Task PollFallbackAsync(int interactionId, string? requestId, CancellationToken ct)
        {
            if (interactionId <= 0)
                return;

            await Task.Delay(PollingInitialDelayMs, ct);

            for (var attempt = 1; attempt <= PollingMaxAttempts; attempt++)
            {
                if (ct.IsCancellationRequested || _disposed)
                    return;

                if (_live.TryGetValue(interactionId, out var currentLive) &&
                    currentLive.IsCompleted)
                {
                    if (EnableVerboseGptPollingLogs)
                    {
                        Console.WriteLine(
                            $"[GPT-POLL] stop: interaction {interactionId} already completed.");
                    }

                    return;
                }

                try
                {
                    var status = await _gptService.GetStatusAsync(interactionId, ct);

                    if (status is null)
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    if (EnableVerboseGptPollingLogs)
                    {
                        Console.WriteLine(
                            $"[GPT-POLL] interaction={interactionId}, attempt={attempt}, status={status.Status}, completed={status.IsCompleted}");
                    }

                    if (!status.IsCompleted)
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    var finalItem = await _gptService.GetByIdAsync(interactionId, ct);

                    if (finalItem is null ||
                        string.IsNullOrWhiteSpace(finalItem.Response))
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                        continue;
                    }

                    Console.WriteLine(
                        $"[GPT POLL] Final interaction retrieved id={interactionId}, responseLen={finalItem.Response.Length}");

                    await CompleteInteractionAsync(
                        finalItem,
                        source: "PollingFallback");

                    return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[GptClientOrchestrator] PollFallbackAsync error for interactionId={interactionId}: {ex}");

                    try
                    {
                        await Task.Delay(PollingIntervalMs, ct);
                    }
                    catch
                    {
                        return;
                    }
                }
            }

            Console.WriteLine(
                $"[GPT-POLL] stop: max attempts reached for interaction {interactionId}.");
        }

        private async Task CompleteInteractionAsync(ClientGptInteractionDTO dto, string source)
        {
            if (dto is null || dto.Id <= 0)
                return;

            if (!_finalRaised.TryAdd(dto.Id, 1))
            {
                Console.WriteLine(
                    $"[GPT COMPLETE] Final already raised for {dto.Id}. Source={source}. Ignored.");

                return;
            }

            var live = _live.GetOrAdd(
                dto.Id,
                _ => new LiveInteractionState(dto.Id));

            live.IsCompleted = true;
            live.HasReceivedHubEvent =
                source.Equals("SignalR", StringComparison.OrdinalIgnoreCase);

            live.Interaction = CloneInteraction(dto);

            live.ResponseBuffer.Clear();
            live.ResponseBuffer.Append(dto.Response ?? string.Empty);

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = dto.Id,
                IsCompleted = true,
                CreatedAt = DateTime.UtcNow,
                Status = "completed",
                Message = "Generation completed."
            };

            if (_currentInteractionId == dto.Id)
            {
                _currentInteractionId = null;
                _currentRequestId = null;
            }

            Console.WriteLine(
                $"[GPT COMPLETE] Final raised id={dto.Id}, source={source}, responseLen={dto.Response?.Length ?? 0}");

            var final = CloneInteraction(dto);

            await RaiseInteractionUpdatedAsync(final);
            await RaiseInteractionCompletedAsync(CloneInteraction(final));
            await RaiseStatusChangedAsync("Generation completed.");
        }

        private async Task RaiseInteractionUpdatedAsync(ClientGptInteractionDTO dto)
        {
            var handler = InteractionUpdated;

            if (handler is null)
                return;

            var subscribers = handler
                .GetInvocationList()
                .Cast<Func<ClientGptInteractionDTO, Task>>();

            foreach (var subscriber in subscribers)
            {
                try
                {
                    await subscriber(CloneInteraction(dto));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[GptClientOrchestrator] InteractionUpdated subscriber failed: {ex.Message}");
                }
            }
        }

        private async Task RaiseInteractionCompletedAsync(ClientGptInteractionDTO dto)
        {
            var handler = InteractionCompleted;

            if (handler is null)
                return;

            var subscribers = handler
                .GetInvocationList()
                .Cast<Func<ClientGptInteractionDTO, Task>>();

            foreach (var subscriber in subscribers)
            {
                try
                {
                    await subscriber(CloneInteraction(dto));
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[GptClientOrchestrator] InteractionCompleted subscriber failed: {ex.Message}");
                }
            }
        }

        private async Task RaiseStatusChangedAsync(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            var handler = StatusChanged;

            if (handler is null)
                return;

            var subscribers = handler
                .GetInvocationList()
                .Cast<Func<string, Task>>();

            foreach (var subscriber in subscribers)
            {
                try
                {
                    await subscriber(message);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(
                        $"[GptClientOrchestrator] StatusChanged subscriber failed: {ex.Message}");
                }
            }
        }

        private static ClientGptInteractionDTO MapCompletedDto(ClientGptInteractionCompletedDTO dto)
        {
            return new ClientGptInteractionDTO
            {
                Id = dto.Id,
                Prompt = dto.Prompt,
                Response = dto.Response,
                PromptHash = dto.PromptHash,
                CreatedAt = dto.CreatedAt,
                Active = dto.Active,
                EventId = dto.EventId,
                CrowdInfoId = dto.CrowdInfoId,
                PlaceId = dto.PlaceId,
                TrafficConditionId = dto.TrafficConditionId,
                WeatherForecastId = dto.WeatherForecastId,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                SourceType = dto.SourceType,
                CrowdLevel = dto.CrowdLevel
            };
        }

        private static ClientGptInteractionDTO CloneInteraction(ClientGptInteractionDTO dto)
        {
            return new ClientGptInteractionDTO
            {
                Id = dto.Id,
                Prompt = dto.Prompt,
                Response = dto.Response,
                PromptHash = dto.PromptHash,
                CreatedAt = dto.CreatedAt,
                Active = dto.Active,
                EventId = dto.EventId,
                CrowdInfoId = dto.CrowdInfoId,
                PlaceId = dto.PlaceId,
                TrafficConditionId = dto.TrafficConditionId,
                WeatherForecastId = dto.WeatherForecastId,
                Latitude = dto.Latitude,
                Longitude = dto.Longitude,
                SourceType = dto.SourceType,
                CrowdLevel = dto.CrowdLevel
            };
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(GptClientOrchestrator));
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            foreach (var sub in _hubSubscriptions)
            {
                try { sub.Dispose(); }
                catch { }
            }

            _hubSubscriptions.Clear();
            _live.Clear();
            _finalRaised.Clear();

            try
            {
                await _multiHub.DisconnectAsync(HubName.Gpt);
            }
            catch
            {
            }

            _connectionLock.Dispose();
        }

        private sealed class LiveInteractionState
        {
            public LiveInteractionState(int interactionId)
            {
                InteractionId = interactionId;
            }

            public int InteractionId { get; }

            public string? RequestId { get; set; }

            public ClientGptInteractionDTO? Interaction { get; set; }

            public GptInteractionService.ClientGptStatusResponseDTO? LastStatus { get; set; }

            public StringBuilder ResponseBuffer { get; } = new();

            public bool IsCompleted { get; set; }

            public bool HasReceivedHubEvent { get; set; }
        }
    }
}









































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
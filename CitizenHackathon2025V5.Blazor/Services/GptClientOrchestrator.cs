using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System.Collections.Concurrent;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class GptClientOrchestrator : IGptClientOrchestrator
    {
        private readonly GptInteractionService _gptService;
        private readonly IMultiHubSignalRClient _multiHub;

        private readonly SemaphoreSlim _connectionLock = new(1, 1);
        private readonly ConcurrentDictionary<int, LiveInteractionState> _live = new();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<ClientGptInteractionDTO?>> _completionWaiters = new();
        private readonly List<IDisposable> _hubSubscriptions = new();

        private bool _disposed;
        private bool _handlersRegistered;

        private int? _currentInteractionId;
        private string? _currentRequestId;

        private static readonly bool EnableVerboseGptPollingLogs = false;
        private const int PollingIntervalMs = 5000;

        public GptClientOrchestrator(GptInteractionService gptService, IMultiHubSignalRClient multiHub)
        {
            _gptService = gptService;
            _multiHub = multiHub;
        }

        public event Func<ClientGptInteractionDTO, Task>? InteractionUpdated;
        public event Func<string, Task>? StatusChanged;

        public bool EnablePollingFallback { get; set; } = true;

        public bool IsHubConnected => _multiHub.IsConnected(HubName.Gpt);
        public bool HasReceivedHubEvent { get; set; }

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

        public async Task<GptRunResult> RunAsync(string prompt, double? latitude = null, double? longitude = null, string languageCode = "fr-FR", CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(prompt))
            {
                return new GptRunResult
                {
                    Started = false,
                    StatusMessage = "Prompt is empty."
                };
            }

            //if (!preferAsyncPipeline)
            //{
            //    var syncFinal = await _gptService.AskGptSync(prompt, latitude, longitude, languageCode, ct);
            //    if (syncFinal is null || syncFinal.Id <= 0)
            //    {
            //        return new GptRunResult
            //        {
            //            Started = false,
            //            StatusMessage = "The synchronous GPT request failed."
            //        };
            //    }

            //    _currentInteractionId = syncFinal.Id;
            //    _currentRequestId = null;

            //    await RaiseInteractionUpdatedAsync(syncFinal);
            //    await RaiseStatusChangedAsync("Generation completed.");

            //    return new GptRunResult
            //    {
            //        Started = true,
            //        InteractionId = syncFinal.Id,
            //        FinalInteraction = syncFinal,
            //        StatusMessage = "Generation completed."
            //    };
            //}

            await EnsureHubAsync(ct);

            var started = await StartAsync(prompt, latitude, longitude, languageCode, ct);
            if (started is null || started.InteractionId <= 0)
            {
                return new GptRunResult
                {
                    Started = false,
                    StatusMessage = "The async GPT request was not accepted."
                };
            }

            _currentInteractionId = started.InteractionId;
            _currentRequestId = started.RequestId;

            ClientGptInteractionDTO? pending = null;
            if (_live.TryGetValue(started.InteractionId, out var livePending) && livePending.Interaction is not null)
                pending = CloneInteraction(livePending.Interaction);

            ClientGptInteractionDTO? finalInteraction = null;

            try
            {
                finalInteraction = await WaitForCompletionAsync(
                    started.InteractionId,
                    started.RequestId,
                    timeout: TimeSpan.FromMinutes(6),
                    ct: ct);
            }
            catch (TimeoutException)
            {
                await RaiseStatusChangedAsync(
                    "Generation still running...");

                return new GptRunResult
                {
                    Started = true,
                    InteractionId = started.InteractionId,
                    RequestId = started.RequestId,
                    PendingInteraction = pending,
                    FinalInteraction = null,
                    StatusMessage =
                        "Generation still running..."
                };
            }

            return new GptRunResult
            {
                Started = true,
                InteractionId = started.InteractionId,
                RequestId = started.RequestId,
                PendingInteraction = pending,
                FinalInteraction = finalInteraction,
                StatusMessage = finalInteraction is not null
                    ? "Generation completed."
                    : started.Message
            };
        }

        public async Task<ClientGptStartResponseDTO?> StartAsync(string prompt, double? latitude = null, double? longitude = null, string languageCode = "fr-FR", CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (string.IsNullOrWhiteSpace(prompt))
                return null;

            await EnsureHubAsync(ct);

            var started = await _gptService.StartGptAsync(prompt: prompt, latitude: latitude, longitude: longitude, languageCode: languageCode, ct: ct);

            if (started is null || started.InteractionId <= 0)
                return null;

            var live = _live.GetOrAdd(
                started.InteractionId,
                _ => new LiveInteractionState(started.InteractionId));

            live.RequestId = started.RequestId;
            live.Interaction = new ClientGptInteractionDTO
            {
                Id = started.InteractionId,
                Prompt = prompt.Trim(),
                Response = string.Empty,
                CreatedAt = started.StartedAtUtc == default ? DateTime.UtcNow : started.StartedAtUtc,
                Active = true,
                Latitude = latitude,
                Longitude = longitude,
                SourceType = "MistralLocal"
            };

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = started.InteractionId,
                IsCompleted = false,
                CreatedAt = started.StartedAtUtc == default ? DateTime.UtcNow : started.StartedAtUtc,
                Status = started.Status ?? "accepted",
                Message = started.Message
            };

            await RaiseInteractionUpdatedAsync(CloneInteraction(live.Interaction));
            await RaiseStatusChangedAsync(started.Message ?? "Request accepted.");

            return started;
        }

        public async Task<ClientGptInteractionDTO?> WaitForCompletionAsync(
            int interactionId,
            string? requestId = null,
            TimeSpan? timeout = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (interactionId <= 0)
                return null;

            var effectiveTimeout = timeout ?? TimeSpan.FromMinutes(3);

            var waiter = _completionWaiters.GetOrAdd(
                interactionId,
                _ => new TaskCompletionSource<ClientGptInteractionDTO?>(
                    TaskCreationOptions.RunContinuationsAsynchronously));

            if (_live.TryGetValue(interactionId, out var existing) &&
                existing.IsCompleted &&
                existing.Interaction is not null)
            {
                waiter.TrySetResult(CloneInteraction(existing.Interaction));
            }

            using var timeoutCts = new CancellationTokenSource(effectiveTimeout);
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            try
            {
                if (EnablePollingFallback)
                {
                    _ = Task.Run(
                        () => PollFallbackAsync(interactionId, requestId, linkedCts.Token),
                        CancellationToken.None);
                }

                var completed = await waiter.Task.WaitAsync(linkedCts.Token);
                return completed;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                Console.WriteLine($"[GptClientOrchestrator] WaitForCompletionAsync timeout for interactionId={interactionId}");

                var fallback = await _gptService.GetByIdAsync(interactionId, CancellationToken.None);
                if (fallback is not null && !string.IsNullOrWhiteSpace(fallback.Response))
                {
                    CompleteInteraction(fallback);
                    return fallback;
                }

                if (_live.TryGetValue(interactionId, out var live) &&
                    live.Interaction is not null &&
                    !string.IsNullOrWhiteSpace(live.Interaction.Response))
                {
                    live.IsCompleted = true;
                    return CloneInteraction(live.Interaction);
                }

                throw new TimeoutException($"GPT completion timeout for interaction {interactionId}.");
            }
            finally
            {
                _completionWaiters.TryRemove(interactionId, out _);
            }
        }

        public async Task<bool> CancelCurrentAsync(CancellationToken ct = default)
        {
            if (!_currentInteractionId.HasValue || _currentInteractionId.Value <= 0)
                return false;

            return await CancelAsync(_currentInteractionId.Value, _currentRequestId, ct);
        }

        public async Task<bool> CancelAsync(
            int interactionId,
            string? requestId = null,
            CancellationToken ct = default)
        {
            ThrowIfDisposed();

            if (interactionId <= 0)
                return false;

            try
            {
                await _gptService.CancelGptRequestAsync(interactionId, requestId ?? string.Empty, ct);

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

                if (_completionWaiters.TryGetValue(interactionId, out var waiter))
                    waiter.TrySetCanceled(ct);

                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GptClientOrchestrator] CancelAsync failed for interactionId={interactionId}: {ex}");
                return false;
            }
        }

        public bool TryGetLiveInteraction(int interactionId, out ClientGptInteractionDTO? interaction)
        {
            interaction = null;

            if (_live.TryGetValue(interactionId, out var live) && live.Interaction is not null)
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
                try { sub.Dispose(); } catch { }
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
            Console.WriteLine($"[GptClientOrchestrator] STARTED received -> InteractionId={dto.InteractionId}, RequestId={dto.RequestId}");
            var live = _live.GetOrAdd(dto.InteractionId, _ => new LiveInteractionState(dto.InteractionId));
            live.HasReceivedHubEvent = true;
            live.RequestId = dto.RequestId;

            if (live.Interaction is null)
            {
                live.Interaction = new ClientGptInteractionDTO
                {
                    Id = dto.InteractionId,
                    Prompt = string.Empty,
                    Response = string.Empty,
                    CreatedAt = dto.StartedAtUtc == default ? DateTime.UtcNow : dto.StartedAtUtc,
                    Active = true,
                    SourceType = "MistralLocal"
                };
            }

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = dto.InteractionId,
                IsCompleted = false,
                CreatedAt = dto.StartedAtUtc == default ? DateTime.UtcNow : dto.StartedAtUtc,
                Status = "started",
                Message = "Generation started."
            };

            await RaiseInteractionUpdatedAsync(CloneInteraction(live.Interaction));
            await RaiseStatusChangedAsync("Generation started.");
        }

        private async Task HandleChunkAsync(ClientGptResponseChunkDTO dto)
        {
            Console.WriteLine($"[GptClientOrchestrator] CHUNK received -> InteractionId={dto.InteractionId}, ChunkLength={dto.Chunk?.Length ?? 0}, IsFinal={dto.IsFinal}");
            var live = _live.GetOrAdd(dto.InteractionId, _ => new LiveInteractionState(dto.InteractionId));
            live.HasReceivedHubEvent = true;
            live.RequestId ??= dto.RequestId;

            if (live.Interaction is null)
            {
                live.Interaction = new ClientGptInteractionDTO
                {
                    Id = dto.InteractionId,
                    Prompt = string.Empty,
                    Response = string.Empty,
                    CreatedAt = DateTime.UtcNow,
                    Active = true,
                    SourceType = "MistralLocal"
                };
            }

            if (!string.IsNullOrEmpty(dto.Chunk))
                live.ResponseBuffer.Append(dto.Chunk);

            live.Interaction.Response = live.ResponseBuffer.ToString();

            await RaiseInteractionUpdatedAsync(CloneInteraction(live.Interaction));

            if (dto.IsFinal)
            {
                live.IsCompleted = true;

                var final = CloneInteraction(live.Interaction);

                if (_completionWaiters.TryGetValue(dto.InteractionId, out var waiter))
                    waiter.TrySetResult(final);

                await RaiseStatusChangedAsync("Generation completed.");
                return;
            }
        }

        private async Task HandleStatusAsync(ClientGptResponseStatusDTO dto)
        {
            if (EnableVerboseGptPollingLogs || dto.IsTerminal)
            {
                Console.WriteLine(
                    $"[GptClientOrchestrator] STATUS received -> InteractionId={dto.InteractionId}, Status={dto.Status}, IsTerminal={dto.IsTerminal}");
            }

            var live = _live.GetOrAdd(dto.InteractionId, _ => new LiveInteractionState(dto.InteractionId));
            live.HasReceivedHubEvent = true;
            live.RequestId ??= dto.RequestId;

            live.LastStatus = new GptInteractionService.ClientGptStatusResponseDTO
            {
                Id = dto.InteractionId,
                IsCompleted = dto.IsTerminal && string.Equals(dto.Status, "completed", StringComparison.OrdinalIgnoreCase),
                CreatedAt = dto.TimestampUtc == default ? DateTime.UtcNow : dto.TimestampUtc,
                Status = dto.Status,
                Message = dto.Message
            };

            if (live.LastStatus?.Status != dto.Status)
            {
                await RaiseStatusChangedAsync(dto.Message ?? dto.Status ?? "GPT status updated.");
            }

            if (dto.IsTerminal &&
                !string.Equals(dto.Status, "completed", StringComparison.OrdinalIgnoreCase) &&
                _completionWaiters.TryGetValue(dto.InteractionId, out var waiter))
            {
                if (string.Equals(dto.Status, "cancelled", StringComparison.OrdinalIgnoreCase))
                    waiter.TrySetCanceled();
                else
                    waiter.TrySetException(new InvalidOperationException(dto.Message ?? $"GPT request ended with status '{dto.Status}'."));
            }
        }

        private async Task HandleCompletedAsync(ClientGptInteractionCompletedDTO dto)
        {
            Console.WriteLine($"[GPT CLIENT] COMPLETED {dto.Id}");
            Console.WriteLine($"[GptClientOrchestrator] COMPLETED received -> InteractionId={dto.Id}");

            var live = _live.GetOrAdd(dto.Id, _ => new LiveInteractionState(dto.Id));
            live.HasReceivedHubEvent = true;

            var completed = MapCompletedDto(dto);

            await CompleteInteractionAsync(completed);
            Console.WriteLine($"[VOICE] COMPLETED ResponseLength={dto.Response?.Length}");
        }

        private async Task CompleteInteractionAsync(ClientGptInteractionDTO dto)
        {
            var live = _live.GetOrAdd(dto.Id, _ => new LiveInteractionState(dto.Id));
            live.HasReceivedHubEvent = true;
            
            live.IsCompleted = true;
            live.Interaction = CloneInteraction(dto);
            live.ResponseBuffer.Clear();
            live.ResponseBuffer.Append(dto.Response ?? string.Empty);


            await RaiseInteractionUpdatedAsync(CloneInteraction(dto));
            await RaiseStatusChangedAsync("Generation completed.");

            if (_completionWaiters.TryGetValue(dto.Id, out var waiter))
                waiter.TrySetResult(CloneInteraction(dto));

            Console.WriteLine($"[VOICE] CompleteInteractionAsync {dto.Response?.Length}");
        }

        private void CompleteInteraction(ClientGptInteractionDTO dto)
        {
            var live = _live.GetOrAdd(dto.Id, _ => new LiveInteractionState(dto.Id));

            live.IsCompleted = true;
            live.Interaction = CloneInteraction(dto);
            live.ResponseBuffer.Clear();
            live.ResponseBuffer.Append(dto.Response ?? string.Empty);

            if (_completionWaiters.TryGetValue(dto.Id, out var waiter))
                waiter.TrySetResult(CloneInteraction(dto));
        }

        private async Task PollFallbackAsync(int interactionId, string? requestId, CancellationToken ct)
        {
            await Task.Delay(4000, ct);

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    if (_live.TryGetValue(interactionId, out var currentLive) &&
                currentLive.IsCompleted)
                    {
                        return;
                    }

                    var status = await _gptService.GetStatusAsync(interactionId, ct);

                    if (status is not null)
                    {
                        await HandleStatusAsync(new ClientGptResponseStatusDTO
                        {
                            InteractionId = interactionId,
                            RequestId = requestId ?? string.Empty,
                            Status = status.IsCompleted ? "completed" : "running",
                            Message = status.Message ?? (status.IsCompleted ? "Generation completed." : "Generation still running."),
                            TimestampUtc = DateTime.UtcNow
                        });

                        if (status.IsCompleted)
                        {
                            var finalItem = await _gptService.GetByIdAsync(interactionId, ct);
                            if (finalItem is not null)
                            {
                                await CompleteInteractionAsync(finalItem);
                                return;
                            }
                        }
                    }

                    if (EnableVerboseGptPollingLogs)
                    {
                        Console.WriteLine($"[GPT-POLL] {interactionId} {status?.Status ?? "unknown"} {DateTime.Now:HH:mm:ss}");
                    }

                    await Task.Delay(PollingIntervalMs, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[GptClientOrchestrator] PollFallbackAsync error for interactionId={interactionId}: {ex}");

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
        }

        private async Task RaiseInteractionUpdatedAsync(ClientGptInteractionDTO dto)
        {
            var handler = InteractionUpdated;
            if (handler is null)
                return;

            var delegates = handler.GetInvocationList()
                .Cast<Func<ClientGptInteractionDTO, Task>>();

            foreach (var subscriber in delegates)
            {
                try { await subscriber(dto); } catch { }
            }
        }

        private async Task RaiseStatusChangedAsync(string message)
        {
            var handler = StatusChanged;
            if (handler is null)
                return;

            var delegates = handler.GetInvocationList()
                .Cast<Func<string, Task>>();

            foreach (var subscriber in delegates)
            {
                try { await subscriber(message); } catch { }
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
                try { sub.Dispose(); } catch { }
            }

            _hubSubscriptions.Clear();
            _completionWaiters.Clear();
            _live.Clear();

            try
            {
                await DisconnectAsync();
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
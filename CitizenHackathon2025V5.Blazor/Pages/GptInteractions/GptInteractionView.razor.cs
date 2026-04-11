using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView
    {
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        protected override string MapId => "leafletMap-gptinteractionview";
        protected override string ScopeKey => "gptinteractionview";
        protected override int DefaultZoom => 14;
        protected override (double lat, double lng) DefaultCenter => (50.29, 4.99);
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override bool ClearAllOnMapReady => true;

        public List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();

        private readonly List<ClientGptInteractionDTO> allGptInteractions = new();
        private readonly List<ClientGptInteractionDTO> visibleGptInteractions = new();

        private int currentIndex;
        private const int PageSize = 20;

        public int SelectedId { get; set; }
        public HubConnection? hubConnection { get; set; }

        private ElementReference ScrollContainerRef;
        private string NewPrompt { get; set; } = string.Empty;
        private string _q = string.Empty;
        private bool _onlyRecent;
        private bool _disposed;
        private bool _isSending;
        private bool _finalSignalReceived;
        private bool _showAiOverlay;

        private DateTime? _overlayShownAtUtc;
        private static readonly TimeSpan MinOverlayDuration = TimeSpan.FromSeconds(2);

        private int? _pendingInteractionId;
        private TaskCompletionSource<bool>? _responseCompletionSource;

        private PeriodicTimer? _elapsedTimer;
        private CancellationTokenSource? _elapsedTimerCts;
        private int _elapsedSeconds;
        private bool _isStreaming;
        private string _streamingPreview = string.Empty;
        private string? _currentRequestId;
        private CancellationTokenSource? _localGenerationCts;

        private enum AiProcessingState
        {
            Idle = 0,
            Sending = 1,
            Generating = 2,
            Success = 3,
            Error = 4
        }

        private AiProcessingState _aiState = AiProcessingState.Idle;
        private string? _aiStatusMessage;

        private bool IsAiBusy =>
            _aiState is AiProcessingState.Sending or AiProcessingState.Generating;

        private bool CanEditPrompt =>
            !IsAiBusy && !_disposed;

        private bool CanSend =>
            !_disposed &&
            !IsAiBusy &&
            !_isSending &&
            !string.IsNullOrWhiteSpace(NewPrompt);

        private bool CanCancel =>
            !_disposed &&
            IsAiBusy &&
            _pendingInteractionId.HasValue;

        private bool ShowStatusBadge =>
            _aiState is AiProcessingState.Sending
            or AiProcessingState.Generating
            or AiProcessingState.Success
            or AiProcessingState.Error;

        private bool ShowAiOverlay =>
            _showAiOverlay || IsAiBusy || _aiState == AiProcessingState.Error;

        private string AddChipCssClass =>
            IsAiBusy ? "chip add-chip chip--disabled" : "chip add-chip";

        private string? AddChipHref =>
            IsAiBusy ? null : "/gptinteractioncreate";

        private DateTime RecentCutoffUtc => DateTime.UtcNow.AddHours(-6);

        private string GetSendButtonText() => _aiState switch
        {
            AiProcessingState.Sending => "Sending...",
            AiProcessingState.Generating when _isStreaming => "Streaming...",
            AiProcessingState.Generating => "Generating...",
            _ => "Send"
        };

        private string GetStatusBadgeText() => _aiState switch
        {
            AiProcessingState.Sending => "Sending",
            AiProcessingState.Generating when _isStreaming => "Streaming",
            AiProcessingState.Generating => "Generating",
            AiProcessingState.Success => "Completed",
            AiProcessingState.Error when string.Equals(_aiStatusMessage, "Generation cancelled", StringComparison.OrdinalIgnoreCase) => "Cancelled",
            AiProcessingState.Error => "Error",
            _ => string.Empty
        };

        private string GetStatusBadgeCssClass() => _aiState switch
        {
            AiProcessingState.Sending => "badge bg-info",
            AiProcessingState.Generating when _isStreaming => "badge bg-primary",
            AiProcessingState.Generating => "badge bg-warning text-dark",
            AiProcessingState.Success => "badge bg-success",
            AiProcessingState.Error when string.Equals(_aiStatusMessage, "Generation cancelled", StringComparison.OrdinalIgnoreCase) => "badge bg-secondary",
            AiProcessingState.Error => "badge bg-danger",
            _ => "badge bg-light text-dark"
        };

        private string GetGlobeCssClass() => _aiState switch
        {
            AiProcessingState.Sending => "oz-gpt-loader-globe--sending",
            AiProcessingState.Generating => "oz-gpt-loader-globe--generating",
            AiProcessingState.Success => "oz-gpt-loader-globe--success",
            AiProcessingState.Error => "oz-gpt-loader-globe--error",
            _ => "oz-gpt-loader-globe--idle"
        };

        protected override async Task OnInitializedAsync()
        {
            var fetched = await GptInteractionService.GetAllInteractions();
            GptInteractions = fetched?.ToList() ?? new List<ClientGptInteractionDTO>();

            allGptInteractions.Clear();
            allGptInteractions.AddRange(GptInteractions);

            visibleGptInteractions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);

            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{hubBaseUrl}/hubs/gptHub", options =>
                {
                    options.AccessTokenProvider = async () =>
                        await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHubHandlers(hubConnection);

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine("[GptInteractionView] Hub started.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GptInteractionView] Hub start failed: {ex}");
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);
        }

        private void RegisterHubHandlers(HubConnection connection)
        {
            connection.On<ClientGptInteractionDTO>("ReceiveGptResponse", async dto =>
            {
                if (dto is null || _disposed)
                    return;

                UpsertInteraction(GptInteractions, dto);
                UpsertInteraction(allGptInteractions, dto);
                UpsertInteraction(visibleGptInteractions, dto);

                if (_pendingInteractionId.HasValue && dto.Id == _pendingInteractionId.Value)
                {
                    _finalSignalReceived = true;
                    _isStreaming = false;
                    _aiState = AiProcessingState.Success;
                    _aiStatusMessage = "Response generated successfully";
                    _responseCompletionSource?.TrySetResult(true);
                }

                await InvokeAsync(StateHasChanged);
            });

            connection.On<int, string>("ReceiveGptResponseStarted", async (interactionId, requestId) =>
            {
                if (_disposed)
                    return;

                if (_pendingInteractionId.HasValue && interactionId == _pendingInteractionId.Value)
                {
                    _currentRequestId = requestId;
                    _isStreaming = true;
                    _streamingPreview = string.Empty;
                    _aiState = AiProcessingState.Generating;
                    _aiStatusMessage = "Mistral is streaming the response";

                    await InvokeAsync(StateHasChanged);
                }
            });

            connection.On<int, string, string, bool>("ReceiveGptResponseChunk", async (interactionId, requestId, chunk, isFinal) =>
            {
                if (_disposed)
                    return;

                if (!_pendingInteractionId.HasValue || interactionId != _pendingInteractionId.Value)
                    return;

                if (!string.IsNullOrWhiteSpace(_currentRequestId) &&
                    !string.Equals(requestId, _currentRequestId, StringComparison.Ordinal))
                    return;

                if (!string.IsNullOrEmpty(chunk))
                {
                    _isStreaming = true;
                    _streamingPreview += chunk;

                    var current = allGptInteractions.FirstOrDefault(x => x.Id == interactionId);
                    if (current is not null)
                    {
                        current.Response = _streamingPreview;
                        UpsertInteraction(GptInteractions, current);
                        UpsertInteraction(allGptInteractions, current);
                        UpsertInteraction(visibleGptInteractions, current);
                    }
                }

                if (isFinal && !_finalSignalReceived)
                {
                    _isStreaming = false;
                    _aiState = AiProcessingState.Success;
                    _aiStatusMessage = "Response generated successfully";
                    _responseCompletionSource?.TrySetResult(true);
                }

                await InvokeAsync(StateHasChanged);
            });

            connection.On<int, string>("ReceiveGptResponseCancelled", async (interactionId, requestId) =>
            {
                if (_disposed)
                    return;

                if (_pendingInteractionId.HasValue && interactionId == _pendingInteractionId.Value)
                {
                    _isStreaming = false;
                    _aiState = AiProcessingState.Error;
                    _aiStatusMessage = "Generation cancelled";
                    _responseCompletionSource?.TrySetCanceled();

                    await InvokeAsync(StateHasChanged);
                }
            });

            connection.On<int, string>("ReceiveGptResponseFailed", async (interactionId, errorMessage) =>
            {
                if (_disposed)
                    return;

                if (_pendingInteractionId.HasValue && interactionId == _pendingInteractionId.Value)
                {
                    _isStreaming = false;
                    _aiState = AiProcessingState.Error;
                    _aiStatusMessage = string.IsNullOrWhiteSpace(errorMessage)
                        ? "Generation failed."
                        : errorMessage;

                    _responseCompletionSource?.TrySetException(new Exception(_aiStatusMessage));

                    await InvokeAsync(StateHasChanged);
                }
            });
        }

        private void LoadMoreItems()
        {
            var next = allGptInteractions
                .Skip(currentIndex)
                .Take(PageSize)
                .ToList();

            if (next.Count == 0)
                return;

            visibleGptInteractions.AddRange(next);
            currentIndex += next.Count;
        }

        private void ClickInfo(int id) => SelectedId = id;

        private async Task HandleScroll()
        {
            if (_disposed)
                return;

            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 &&
                currentIndex < allGptInteractions.Count)
            {
                LoadMoreItems();
                await InvokeAsync(StateHasChanged);
            }
        }

        private IEnumerable<ClientGptInteractionDTO> FilterGpt(IEnumerable<ClientGptInteractionDTO> source)
        {
            var q = _q?.Trim();

            return source
                .Where(x =>
                    string.IsNullOrWhiteSpace(q) ||
                    (!string.IsNullOrWhiteSpace(x.Prompt) && x.Prompt.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(x.Response) && x.Response.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !_onlyRecent || x.CreatedAt >= RecentCutoffUtc);
        }

        private async Task HandleAskGpt()
        {
            if (_disposed || _isSending || IsAiBusy)
                return;

            var prompt = NewPrompt?.Trim();
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            try
            {
                _isSending = true;
                _localGenerationCts = new CancellationTokenSource();

                ResetStreamingState();

                _aiState = AiProcessingState.Sending;
                _aiStatusMessage = "Message received. Sending prompt to Mistral";
                _pendingInteractionId = null;
                _responseCompletionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

                await StartElapsedTimerAsync();
                await ShowOverlayAsync();

                _ = PromoteSendingToGeneratingAsync();

                var created = await GptInteractionService.AskGpt(
                    new ClientGptInteractionDTO
                    {
                        Prompt = prompt
                    },
                    latitude: null,
                    longitude: null,
                    ct: CancellationToken.None);

                if (created is null || created.Id <= 0)
                    throw new InvalidOperationException("The GPT interaction could not be created.");

                UpsertInteraction(GptInteractions, created);
                UpsertInteraction(allGptInteractions, created);
                UpsertInteraction(visibleGptInteractions, created);

                SelectedId = created.Id;
                _pendingInteractionId = created.Id;

                _aiState = AiProcessingState.Generating;
                _aiStatusMessage = "Mistral is generating a response";
                await InvokeAsync(StateHasChanged);

                if (!string.IsNullOrWhiteSpace(created.Response))
                {
                    _finalSignalReceived = true;
                    _aiState = AiProcessingState.Success;
                    _aiStatusMessage = "Response generated successfully";
                    NewPrompt = string.Empty;

                    await InvokeAsync(StateHasChanged);
                    await Task.Delay(500);
                    return;
                }

                var completedTask = await Task.WhenAny(
                    _responseCompletionSource.Task,
                    Task.Delay(TimeSpan.FromMinutes(9), _localGenerationCts.Token));

                if (completedTask != _responseCompletionSource.Task)
                    throw new TimeoutException("Mistral took too long to respond.");

                NewPrompt = string.Empty;
                await InvokeAsync(StateHasChanged);
                await Task.Delay(500);
            }
            catch (TaskCanceledException)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Generation cancelled";
                await InvokeAsync(StateHasChanged);
                await Task.Delay(800);
            }
            catch (Exception ex)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = $"Error: {ex.Message}";
                await InvokeAsync(StateHasChanged);
                await Task.Delay(1200);

                Console.Error.WriteLine($"[GPT] HandleAskGpt failed: {ex}");
            }
            finally
            {
                await StopElapsedTimerAsync();
                await HideOverlayAsync();

                _isSending = false;
                _pendingInteractionId = null;
                _responseCompletionSource = null;
                _currentRequestId = null;

                _localGenerationCts?.Dispose();
                _localGenerationCts = null;

                _aiStatusMessage = null;
                _aiState = AiProcessingState.Idle;
                _isStreaming = false;
                _finalSignalReceived = false;

                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task PromoteSendingToGeneratingAsync()
        {
            try
            {
                await Task.Delay(2000);

                if (!_disposed && _aiState == AiProcessingState.Sending)
                {
                    _aiState = AiProcessingState.Generating;
                    _aiStatusMessage = "Mistral is generating a response";
                    await InvokeAsync(StateHasChanged);
                }
            }
            catch
            {
            }
        }

        private async Task HandleCancelGeneration()
        {
            if (_disposed || !CanCancel)
                return;

            try
            {
                _aiStatusMessage = "Cancelling generation...";
                await InvokeAsync(StateHasChanged);

                if (_pendingInteractionId.HasValue)
                {
                    await GptInteractionService.CancelGptRequestAsync(
                        _pendingInteractionId.Value,
                        _currentRequestId,
                        CancellationToken.None);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GPT] Cancel failed: {ex}");
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Failed to cancel generation.";
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task ShowOverlayAsync()
        {
            _showAiOverlay = true;
            _overlayShownAtUtc = DateTime.UtcNow;

            await InvokeAsync(StateHasChanged);
            await Task.Yield();
            await Task.Delay(50);
        }

        private async Task HideOverlayAsync()
        {
            if (_overlayShownAtUtc.HasValue)
            {
                var elapsed = DateTime.UtcNow - _overlayShownAtUtc.Value;
                var remaining = MinOverlayDuration - elapsed;

                if (remaining > TimeSpan.Zero)
                    await Task.Delay(remaining);
            }

            _showAiOverlay = false;
            _overlayShownAtUtc = null;
            await InvokeAsync(StateHasChanged);
        }

        private async Task StartElapsedTimerAsync()
        {
            await StopElapsedTimerAsync();

            _elapsedSeconds = 0;
            _elapsedTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
            _elapsedTimerCts = new CancellationTokenSource();

            _ = Task.Run(async () =>
            {
                try
                {
                    while (_elapsedTimer is not null &&
                           _elapsedTimerCts is not null &&
                           await _elapsedTimer.WaitForNextTickAsync(_elapsedTimerCts.Token))
                    {
                        _elapsedSeconds++;
                        await InvokeAsync(StateHasChanged);
                    }
                }
                catch (OperationCanceledException)
                {
                }
                catch
                {
                }
            });
        }

        private Task StopElapsedTimerAsync()
        {
            try
            {
                _elapsedTimerCts?.Cancel();
            }
            catch
            {
            }

            _elapsedTimerCts?.Dispose();
            _elapsedTimerCts = null;

            _elapsedTimer?.Dispose();
            _elapsedTimer = null;

            return Task.CompletedTask;
        }

        private void ResetStreamingState()
        {
            _elapsedSeconds = 0;
            _isStreaming = false;
            _streamingPreview = string.Empty;
            _currentRequestId = null;
            _finalSignalReceived = false;
        }

        private static void UpsertInteraction(List<ClientGptInteractionDTO> list, ClientGptInteractionDTO dto)
        {
            var index = list.FindIndex(x => x.Id == dto.Id);

            if (index >= 0)
                list[index] = dto;
            else
                list.Insert(0, dto);
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        private Task PreventAddNavigationWhenBusy(MouseEventArgs _)
        {
            return Task.CompletedTask;
        }

        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;

            await StopElapsedTimerAsync();

            try
            {
                _localGenerationCts?.Cancel();
            }
            catch
            {
            }

            _localGenerationCts?.Dispose();
            _localGenerationCts = null;

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
                hubConnection = null;
            }
        }
    }
}

































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView : IAsyncDisposable
    {
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;

        protected override string MapId => "leafletMap-gptinteractionview";
        protected override string ScopeKey => "gptinteractionview";
        protected override int DefaultZoom => 14;
        protected override (double lat, double lng) DefaultCenter => (50.29, 4.99);
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override bool ClearAllOnMapReady => true;

        public List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();

        private readonly List<ClientGptInteractionDTO> allGptInteractions = new();
        protected readonly List<ClientGptInteractionDTO> visibleGptInteractions = new();

        private const int PageSize = 20;
        private int currentIndex;

        private double? _gptLatitude;
        private double? _gptLongitude;

        private int? _currentInteractionId;
        private string? _currentRequestId;
        private CancellationTokenSource? _currentPollingCts;

        // false = mode sync direct
        // true  = mode async + polling de secours
        // Tu pourras plus tard remplacer le polling par SignalR streaming.
        private const bool PreferAsyncPipeline = true;

        public int SelectedId { get; set; }

        private ElementReference ScrollContainerRef;

        protected string NewPrompt { get; set; } = string.Empty;
        protected string _q = string.Empty;
        protected bool _onlyRecent;

        private bool _disposed;
        private bool _isSending;
        private bool _showAiOverlay;

        private DateTime? _overlayShownAtUtc;
        private static readonly TimeSpan MinOverlayDuration = TimeSpan.FromSeconds(2);

        private PeriodicTimer? _elapsedTimer;
        private CancellationTokenSource? _elapsedTimerCts;
        protected int _elapsedSeconds;

        private DotNetObjectReference<GptInteractionView>? _dotNetRef;

        private bool _voiceSupported;
        private bool _isListening;
        private string _voiceInterimText = string.Empty;

        private enum AiProcessingState
        {
            Idle = 0,
            Generating = 1,
            Success = 2,
            Error = 3
        }

        private AiProcessingState _aiState = AiProcessingState.Idle;
        protected string? _aiStatusMessage;

        private bool IsAiBusy => _aiState == AiProcessingState.Generating;

        private bool CanEditPrompt =>
            !_disposed && !IsAiBusy && !_isSending;

        private bool CanSend =>
            !_disposed &&
            !IsAiBusy &&
            !_isSending &&
            !string.IsNullOrWhiteSpace(NewPrompt);

        private bool CanUseVoice =>
            !_disposed &&
            !IsAiBusy &&
            !_isSending &&
            _voiceSupported;

        private string VoiceButtonText =>
            !_voiceSupported
                ? "Micro unavailable"
                : _isListening
                    ? "Stop dictation"
                    : "Start dictation";

        private string VoiceStatusText =>
            !_voiceSupported
                ? "Voice recognition not supported by this browser."
                : _isListening
                    ? "Listening..."
                    : string.IsNullOrWhiteSpace(_voiceInterimText)
                        ? string.Empty
                        : _voiceInterimText;

        private bool ShowStatusBadge =>
            _aiState is AiProcessingState.Generating
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
            AiProcessingState.Generating => PreferAsyncPipeline ? "Generating (async)..." : "Generating...",
            _ => "Send"
        };

        private string GetStatusBadgeText() => _aiState switch
        {
            AiProcessingState.Generating => PreferAsyncPipeline ? "Generating async" : "Generating",
            AiProcessingState.Success => "Completed",
            AiProcessingState.Error => "Error",
            _ => string.Empty
        };

        private string GetStatusBadgeCssClass() => _aiState switch
        {
            AiProcessingState.Generating => "badge bg-warning text-dark",
            AiProcessingState.Success => "badge bg-success",
            AiProcessingState.Error => "badge bg-danger",
            _ => "badge bg-light text-dark"
        };

        private string GetGlobeCssClass() => _aiState switch
        {
            AiProcessingState.Generating => "oz-gpt-loader-globe--generating",
            AiProcessingState.Success => "oz-gpt-loader-globe--success",
            AiProcessingState.Error => "oz-gpt-loader-globe--error",
            _ => "oz-gpt-loader-globe--idle"
        };

        protected override async Task OnInitializedAsync()
        {
            _gptLatitude = DefaultCenter.lat;
            _gptLongitude = DefaultCenter.lng;

            _dotNetRef = DotNetObjectReference.Create(this);

            await LoadInteractionsAsync();
            await DetectVoiceSupportAsync();
        }

        private async Task LoadInteractionsAsync()
        {
            var fetched = await GptInteractionService.GetAllInteractions();
            GptInteractions = fetched?.ToList() ?? new List<ClientGptInteractionDTO>();

            allGptInteractions.Clear();
            allGptInteractions.AddRange(GptInteractions);

            visibleGptInteractions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);
        }

        private async Task DetectVoiceSupportAsync()
        {
            try
            {
                _voiceSupported = await JS.InvokeAsync<bool>("gptVoice.isSupported");
            }
            catch
            {
                _voiceSupported = false;
            }

            await InvokeAsync(StateHasChanged);
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
                ResetUiState();

                _aiState = AiProcessingState.Generating;
                _aiStatusMessage = PreferAsyncPipeline
                    ? "Mistral request accepted. Waiting for completion..."
                    : "Mistral is generating a response";

                await StartElapsedTimerAsync();
                await ShowOverlayAsync();
                await InvokeAsync(StateHasChanged);

                if (PreferAsyncPipeline)
                {
                    await HandleAskGptAsyncPipeline(prompt);
                }
                else
                {
                    await HandleAskGptSyncPipeline(prompt);
                }

                _aiState = AiProcessingState.Success;
                _aiStatusMessage = "Response generated successfully";
                await InvokeAsync(StateHasChanged);

                await Task.Delay(600);

                await StopElapsedTimerAsync();

                _aiState = AiProcessingState.Idle;
                _aiStatusMessage = null;

                await InvokeAsync(StateHasChanged);
            }
            catch (OperationCanceledException)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Generation cancelled.";
                await InvokeAsync(StateHasChanged);

                await Task.Delay(600);
                await StopElapsedTimerAsync();

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = $"Error: {ex.Message}";
                await InvokeAsync(StateHasChanged);

                Console.Error.WriteLine($"[GPT] HandleAskGpt failed: {ex}");

                await Task.Delay(900);
                await StopElapsedTimerAsync();

                await InvokeAsync(StateHasChanged);
            }
            finally
            {
                _isSending = false;
            }
        }

        private async Task HandleAskGptSyncPipeline(string prompt)
        {
            var result = await GptInteractionService.AskGptSync(
                prompt,
                latitude: _gptLatitude,
                longitude: _gptLongitude,
                ct: CancellationToken.None);

            if (result is null || result.Id <= 0)
                throw new InvalidOperationException("The GPT response was empty or invalid.");

            ApplyFinalInteraction(result);
        }

        private async Task HandleAskGptAsyncPipeline(string prompt)
        {
            await StopCurrentPollingAsync();

            var started = await GptInteractionService.StartGptAsync(
                prompt,
                latitude: _gptLatitude,
                longitude: _gptLongitude,
                ct: CancellationToken.None);

            if (started is null || !started.Accepted || started.InteractionId <= 0)
                throw new InvalidOperationException("The async GPT request was not accepted by the API.");

            _currentInteractionId = started.InteractionId;
            _currentRequestId = started.RequestId;

            _aiStatusMessage = string.IsNullOrWhiteSpace(started.Message)
                ? "Request accepted. Waiting for result..."
                : started.Message;

            await InvokeAsync(StateHasChanged);

            _currentPollingCts = new CancellationTokenSource();

            var result = await PollUntilCompletedAsync(
                started.InteractionId,
                _currentPollingCts.Token);

            if (result is null || result.Id <= 0)
                throw new InvalidOperationException("The final GPT interaction could not be retrieved.");

            ApplyFinalInteraction(result);
        }

        private async Task<ClientGptInteractionDTO?> PollUntilCompletedAsync(int interactionId, CancellationToken ct)
        {
            const int maxAttempts = 180; // ~ 3 minutes si intervalle 1s
            const int delayMs = 1000;

            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                var status = await GptInteractionService.GetStatusAsync(interactionId, ct);

                if (status is not null)
                {
                    _aiStatusMessage = string.IsNullOrWhiteSpace(status.Message)
                        ? $"Generation in progress... ({attempt})"
                        : status.Message;

                    await InvokeAsync(StateHasChanged);

                    if (status.IsCompleted)
                    {
                        var finalItem = await GptInteractionService.GetByIdAsync(interactionId, ct);
                        if (finalItem is not null)
                            return finalItem;
                    }
                }

                await Task.Delay(delayMs, ct);
            }

            throw new TimeoutException("The GPT response did not complete in time.");
        }

        private void ApplyFinalInteraction(ClientGptInteractionDTO result)
        {
            UpsertInteraction(GptInteractions, result);
            UpsertInteraction(allGptInteractions, result);
            UpsertInteraction(visibleGptInteractions, result);

            SelectedId = result.Id;
            NewPrompt = string.Empty;
        }

        private async Task ToggleVoiceAsync()
        {
            if (_disposed || IsAiBusy || _isSending)
                return;

            if (!_voiceSupported)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Voice recognition is not supported by this browser.";
                await InvokeAsync(StateHasChanged);
                return;
            }

            if (_isListening)
            {
                await StopVoiceAsync();
                return;
            }

            try
            {
                var result = await JS.InvokeAsync<VoiceStartResult>(
                    "gptVoice.start",
                    _dotNetRef,
                    "fr-BE");

                if (result is null || !result.Ok)
                {
                    _aiState = AiProcessingState.Error;
                    _aiStatusMessage = string.IsNullOrWhiteSpace(result?.Error)
                        ? "Unable to start voice recognition."
                        : result.Error;

                    _isListening = false;
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                _isListening = true;
                _aiStatusMessage = "Voice dictation started.";
                await InvokeAsync(StateHasChanged);
            }
            catch (JSException ex)
            {
                _isListening = false;
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = $"Voice error: {ex.Message}";
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task StopVoiceAsync()
        {
            try
            {
                await JS.InvokeVoidAsync("gptVoice.stop");
            }
            catch
            {
            }

            _isListening = false;
            _voiceInterimText = string.Empty;
            await InvokeAsync(StateHasChanged);
        }

        [JSInvokable]
        public async Task OnVoiceRecognitionResult(string finalText, string interimText)
        {
            if (_disposed)
                return;

            if (!string.IsNullOrWhiteSpace(finalText))
            {
                if (string.IsNullOrWhiteSpace(NewPrompt))
                    NewPrompt = finalText.Trim();
                else
                    NewPrompt = $"{NewPrompt.Trim()} {finalText.Trim()}".Trim();
            }

            _voiceInterimText = interimText ?? string.Empty;

            await InvokeAsync(StateHasChanged);
        }

        [JSInvokable]
        public async Task OnVoiceRecognitionError(string errorMessage)
        {
            if (_disposed)
                return;

            _isListening = false;
            _voiceInterimText = string.Empty;
            _aiState = AiProcessingState.Error;
            _aiStatusMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? "Voice recognition failed."
                : errorMessage;

            await InvokeAsync(StateHasChanged);
        }

        [JSInvokable]
        public async Task OnVoiceStopped()
        {
            if (_disposed)
                return;

            _isListening = false;
            _voiceInterimText = string.Empty;
            await InvokeAsync(StateHasChanged);
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

        private async Task StopElapsedTimerAsync()
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

            if (_showAiOverlay)
                await HideOverlayAsync();
        }

        private async Task StopCurrentPollingAsync()
        {
            try
            {
                _currentPollingCts?.Cancel();
            }
            catch
            {
            }

            _currentPollingCts?.Dispose();
            _currentPollingCts = null;

            await Task.CompletedTask;
        }

        private void ResetUiState()
        {
            _elapsedSeconds = 0;
            _currentInteractionId = null;
            _currentRequestId = null;
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

        private Task PreventAddNavigationWhenBusy(MouseEventArgs _) => Task.CompletedTask;

        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;

            await StopCurrentPollingAsync();
            await StopElapsedTimerAsync();

            if (_isListening)
                await StopVoiceAsync();

            _dotNetRef?.Dispose();
            _dotNetRef = null;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_disposed)
                await OnBeforeDisposeAsync();
        }

        private sealed class VoiceStartResult
        {
            public bool Ok { get; set; }
            public string? Error { get; set; }
        }
    }
}




















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





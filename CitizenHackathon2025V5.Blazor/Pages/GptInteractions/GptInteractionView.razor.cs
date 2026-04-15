using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView
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

        private readonly List<ClientGptInteractionDTO> _allGptInteractions = new();
        protected readonly List<ClientGptInteractionDTO> visibleGptInteractions = new();

        private const int PageSize = 20;
        private int _currentIndex;

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
            !_disposed && !_isSending && !IsAiBusy;

        private bool CanSend =>
            !_disposed &&
            !_isSending &&
            !IsAiBusy &&
            !string.IsNullOrWhiteSpace(NewPrompt);

        private bool ShowStatusBadge =>
            _aiState is AiProcessingState.Generating
            or AiProcessingState.Success
            or AiProcessingState.Error;

        private bool ShowAiOverlay => _showAiOverlay;

        private string AddChipCssClass =>
            IsAiBusy ? "chip add-chip chip--disabled" : "chip add-chip";

        private string? AddChipHref =>
            IsAiBusy ? null : "/gptinteractioncreate";

        private DateTime RecentCutoffUtc => DateTime.UtcNow.AddHours(-6);

        private string GetSendButtonText() => _aiState switch
        {
            AiProcessingState.Generating => "Generating...",
            _ => "Send"
        };

        private string GetStatusBadgeText() => _aiState switch
        {
            AiProcessingState.Generating => "Generating",
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
            var fetched = await GptInteractionService.GetAllInteractions();
            GptInteractions = fetched?.ToList() ?? new List<ClientGptInteractionDTO>();

            _allGptInteractions.Clear();
            _allGptInteractions.AddRange(GptInteractions);

            visibleGptInteractions.Clear();
            _currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);
        }

        private void LoadMoreItems()
        {
            var next = _allGptInteractions
                .Skip(_currentIndex)
                .Take(PageSize)
                .ToList();

            if (next.Count == 0)
                return;

            visibleGptInteractions.AddRange(next);
            _currentIndex += next.Count;
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
                _currentIndex < _allGptInteractions.Count)
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
                    (!string.IsNullOrWhiteSpace(x.Prompt) &&
                     x.Prompt.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(x.Response) &&
                     x.Response.Contains(q, StringComparison.OrdinalIgnoreCase)))
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
                _aiStatusMessage = "Mistral is generating a response";

                await StartElapsedTimerAsync();
                await ShowOverlayAsync();
                await InvokeAsync(StateHasChanged);

                var result = await GptInteractionService.AskGpt(
                    new ClientGptInteractionDTO
                    {
                        Prompt = prompt
                    },
                    latitude: null,
                    longitude: null,
                    ct: CancellationToken.None);

                if (result is null || result.Id <= 0)
                    throw new InvalidOperationException("The GPT response was empty or invalid.");

                UpsertInteraction(GptInteractions, result);
                UpsertInteraction(_allGptInteractions, result);
                UpsertInteraction(visibleGptInteractions, result);

                SelectedId = result.Id;
                NewPrompt = string.Empty;

                _aiState = AiProcessingState.Success;
                _aiStatusMessage = "Response generated successfully";

                await StopElapsedTimerAsync();
                await HideOverlayAsync();

                await InvokeAsync(StateHasChanged);
            }
            catch (OperationCanceledException)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = "Generation cancelled.";

                await StopElapsedTimerAsync();
                await HideOverlayAsync();

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                _aiState = AiProcessingState.Error;
                _aiStatusMessage = $"Error: {ex.Message}";

                await StopElapsedTimerAsync();
                await HideOverlayAsync();

                await InvokeAsync(StateHasChanged);

                Console.Error.WriteLine($"[GPT] HandleAskGpt failed: {ex}");
            }
            finally
            {
                _isSending = false;
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

            await Task.CompletedTask;
        }

        private void ResetUiState()
        {
            _elapsedSeconds = 0;
            _aiStatusMessage = null;
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

            await StopElapsedTimerAsync();

            if (_showAiOverlay)
                await HideOverlayAsync();
        }
    }
}




















































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





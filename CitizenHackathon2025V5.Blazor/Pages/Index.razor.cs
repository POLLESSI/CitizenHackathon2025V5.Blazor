using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class Index : OutZenMapPageBase
    {
#nullable disable
        [Inject] public MessageService MessageService { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public TrafficConditionService TrafficConditionService { get; set; } = default!;
        [Inject] public CrowdInfoService CrowdInfoService { get; set; } = default!;
        [Inject] public CrowdInfoCalendarService CrowdInfoCalendarService { get; set; } = default!;
        [Inject] public ICrowdInfoAntennaService CrowdInfoAntennaService { get; set; } = default!;
        [Inject] public EventService EventService { get; set; } = default!;
        [Inject] public SuggestionService SuggestionService { get; set; } = default!;
        [Inject] public PlaceService PlaceService { get; set; } = default!;
        [Inject] public WeatherForecastService WeatherForecastService { get; set; } = default!;
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public IGptClientOrchestrator GptClientOrchestrator { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        protected override string ScopeKey => "home";
        protected override string MapId => "leafletMap-home";

        protected override bool EnableHybrid => true;
        protected override bool EnableCluster => true;
        protected override bool EnableWeatherLegend => true;
        protected override int DefaultZoom => 12;
        protected override int HybridThreshold => 13;

        public MessageFormModel Model { get; } = new();
        private bool _isSendingPrompt;
        protected bool IsSending { get; set; }
        protected string NewMessage { get; set; } = string.Empty;

        private List<ClientTrafficConditionDTO> _traffic = new();
        private List<ClientCrowdInfoDTO> _crowds = new();
        private List<ClientEventDTO> _events = new();
        private List<ClientSuggestionDTO> _suggestions = new();
        private List<ClientPlaceDTO> _places = new();
        private List<ClientWeatherForecastDTO> _weather = new();
        private List<ClientCrowdInfoCalendarDTO> _allCal = new();
        protected List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();

        private List<ClientCrowdInfoAntennaDTO> _allAntennas = new();
        private readonly ConcurrentDictionary<int, ClientAntennaCountsDTO> _countsByAntenna = new();
        private readonly ConcurrentQueue<(int AntennaId, ClientAntennaCountsDTO Counts)> _pendingCountsUntilMap = new();

        private DotNetObjectReference<Index> _dotNetRef;
        private HubConnection _antennaHub;

        private PeriodicTimer _timer;
        private bool _timerStarted;
        private bool _disposed;

        private long _lastToggleMs;
        private bool _dragWired;
        private bool drawerOpen;
        private bool _historyCollapsed = true;
        private bool _criticalAlertSending;
        private int? _selectedPlaceId = 1;
        private string _selectedPlaceName = "Maison de famille";
        private string _userPrompt = "";
        private string _gptStatusMessage;
        private string _criticalAlertStatus;

        private string _q = "";
        private readonly List<ClientGptInteractionDTO> _all = new();
        private readonly List<ClientGptInteractionDTO> _visible = new();
        private int _currentIndex = 0;
        private const int PageSize = 20;
        private const int MaxVisibleGptItems = 30;
        private int VisibleCount => _visible.Count;
        private bool CanLoadMore => _currentIndex < _all.Count;

        [JSInvokable]
        public Task SelectSuggestionFromMap(int suggestionId)
        {
            Console.WriteLine($"[Map] Suggestion clicked: {suggestionId}");
            return Task.CompletedTask;
        }

        protected override async Task OnInitializedAsync()
        {
            GptClientOrchestrator.InteractionUpdated += OnGptInteractionUpdatedAsync;
            GptClientOrchestrator.StatusChanged += OnGptStatusChangedAsync;

            var trafficTask = TrafficConditionService.GetLatestTrafficConditionAsync();
            var crowdTask = CrowdInfoService.GetLatestCrowdInfoNonNullAsync();
            var eventTask = EventService.GetLatestEventAsync();
            var suggestionTask = SuggestionService.GetLatestSuggestionAsync();
            var placeTask = PlaceService.GetLatestPlaceAsync();
            var weatherTask = WeatherForecastService.GetLatestWeatherForecastAsync();
            Task<List<ClientGptInteractionDTO>> gptTask = SafeGetGptAsync();

            await Task.WhenAll(trafficTask, crowdTask, eventTask, suggestionTask, placeTask, weatherTask, gptTask);

            _traffic = trafficTask.Result ?? new();
            _crowds = crowdTask.Result ?? new();
            _events = (eventTask.Result ?? Enumerable.Empty<ClientEventDTO>()).ToList();
            _suggestions = (suggestionTask.Result ?? Enumerable.Empty<ClientSuggestionDTO>()).ToList();
            _places = (placeTask.Result ?? Enumerable.Empty<ClientPlaceDTO>()).ToList();
            _weather = (weatherTask.Result ?? Enumerable.Empty<ClientWeatherForecastDTO>()).ToList();
            GptInteractions = gptTask.Result ?? new();

            _all.Clear();
            _all.AddRange(GptInteractions.OrderByDescending(x => x.CreatedAt));
            _visible.Clear();
            _currentIndex = 0;
            LoadMore();

            static bool HasValidCoord(double lat, double lng)
                => lat is >= 49.45 and <= 51.6 && lng is >= 2.3 and <= 6.6;

            _places = _places.Where(p => HasValidCoord(p.Latitude, p.Longitude)).ToList();
            _events = _events.Where(e => HasValidCoord(e.Latitude, e.Longitude)).ToList();
            _crowds = _crowds.Where(c => HasValidCoord(c.Latitude, c.Longitude)).ToList();

            _allCal = (await CrowdInfoCalendarService.GetAllSafeAsync()).ToList();

            await SafeLoadAntennasAsync();
            await EnsureAntennaHubAsync();

            try
            {
                await GptClientOrchestrator.EnsureHubAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME GPT] EnsureHubAsync failed: {ex}");
            }

            StartCalendarTimer();

            await InvokeAsync(StateHasChanged);
            await NotifyDataLoadedAsync(fit: true);
        }

        protected override async Task SeedAsync(bool fit)
        {
            var payload = new
            {
                events = _events,
                places = _places,
                crowds = _crowds,
                suggestions = _suggestions,
                traffic = _traffic,
                weather = _weather,
                gpt = Array.Empty<object>()
            };

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateBundleMarkers", payload, 80, ScopeKey);

            var nowUtc = DateTime.UtcNow;
            var todayCalendar = _allCal.Where(x => IsNowActive(x, nowUtc)).ToList();

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", todayCalendar, ScopeKey);

            if (fit)
            {
                try { await JS.InvokeVoidAsync("OutZenInterop.fitToBundles", ScopeKey, new { maxZoom = 16 }); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.activateHybridAndZoom", ScopeKey, HybridThreshold); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey); } catch { }
            }
        }

        private async Task SendCriticalCrowdAlertAsync()
        {
            if (_selectedPlaceId is null or <= 0)
            {
                ToastService.ShowWarning("No place selected.");
                return;
            }

            _criticalAlertSending = true;

            try
            {
                var result = await CriticalAlertService.SendCriticalAlertAsync(
                     _selectedPlaceId.Value,
                     $"Manual critical alert for {_selectedPlaceName}");

                if (result.Ok)
                {
                    ToastService.ShowError(
                        $"CRITICAL CROWD ALERT sent for {_selectedPlaceName}.",
                        settings =>
                        {
                            settings.Timeout = 0; // remains displayed until manually closed
                            settings.ShowProgressBar = true;
                        });

                    _criticalAlertStatus = $"Critical alert active for {_selectedPlaceName}";
                }
                else
                {
                    Console.Error.WriteLine(result.Error);
                    ToastService.ShowWarning("Alert could not be sent. Check browser/API console.");
                }
            }
            finally
            {
                _criticalAlertSending = false;
            }
        }

        protected override async Task OnMapReadyAsync()
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);
            try { await JS.InvokeVoidAsync("OutZenInterop.registerDotNetRef", ScopeKey, _dotNetRef); } catch { }

            foreach (var a in _allAntennas)
            {
                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker", new
                {
                    Id = a.Id,
                    Latitude = a.Latitude,
                    Longitude = a.Longitude,
                    Name = a.Name,
                    Description = "Waiting for counts…"
                }, ScopeKey);
            }

            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                try { await ApplyAntennaCriticalOverlayAsync(item.AntennaId, item.Counts); } catch { }
            }

            try { await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey); } catch { }
        }

        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;
            _timerStarted = false;

            try { _timer?.Dispose(); } catch { }
            try { if (_antennaHub is not null) await _antennaHub.DisposeAsync(); } catch { }

            GptClientOrchestrator.InteractionUpdated -= OnGptInteractionUpdatedAsync;
            GptClientOrchestrator.StatusChanged -= OnGptStatusChangedAsync;

            try { await GptClientOrchestrator.CancelCurrentAsync(); } catch { }

            try { await JS.InvokeVoidAsync("OutZenInterop.unregisterDotNetRef", ScopeKey); } catch { }
            try { _dotNetRef?.Dispose(); } catch { }
            _dotNetRef = null;
        }

        private void StartCalendarTimer()
        {
            _timer ??= new PeriodicTimer(TimeSpan.FromSeconds(30));
            _timerStarted = true;

            _ = Task.Run(async () =>
            {
                while (_timerStarted && await _timer.WaitForNextTickAsync())
                {
                    try { await InvokeAsync(RefreshCalendarMarkersNowAsync); } catch { }
                }
            });
        }

        private async Task RefreshCalendarMarkersNowAsync()
        {
            if (!IsMapBooted) return;

            var nowUtc = DateTime.UtcNow;
            var active = _allCal.Where(x => IsNowActive(x, nowUtc)).ToList();

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", active, ScopeKey);

            var activeIds = active.Select(x => $"cc:{x.Id}").ToList();
            await JS.InvokeVoidAsync("OutZenInterop.pruneCrowdCalendarMarkers", activeIds, ScopeKey);
        }

        private async Task EnsureAntennaHubAsync()
        {
            if (_antennaHub is not null) return;

            var hubUrl = HubUrls.Build(HubPaths.AntennaConnection);

            _antennaHub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
                })
                .WithAutomaticReconnect()
                .Build();

            _antennaHub.On<ClientAntennaCountsUpdateDTO>(
                CrowdInfoAntennaConnectionHubMethods.ToClient.AntennaCountsUpdated,
                async msg =>
                {
                    _countsByAntenna[msg.AntennaId] = msg.Counts;

                    if (!IsMapBooted)
                    {
                        _pendingCountsUntilMap.Enqueue((msg.AntennaId, msg.Counts));
                        return;
                    }

                    await ApplyAntennaCriticalOverlayAsync(msg.AntennaId, msg.Counts);
                });

            await _antennaHub.StartAsync();
        }

        private async Task ApplyAntennaCriticalOverlayAsync(int antennaId, ClientAntennaCountsDTO counts)
        {
            var antenna = _allAntennas.FirstOrDefault(a => a.Id == antennaId);
            if (antenna is null) return;

            var level = ComputeLevelByCapacity(antenna, counts.ActiveConnections);

            if (level == (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.High)
            {
                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker", new
                {
                    Id = antennaId,
                    Latitude = antenna.Latitude,
                    Longitude = antenna.Longitude,
                    Name = antenna.Name,
                    Description = $"{counts.ActiveConnections} connexions • {counts.UniqueDevices} devices"
                }, ScopeKey);
            }
            else
            {
                await JS.InvokeVoidAsync("OutZenInterop.removeAntennaMarker", $"ant:{antennaId}", ScopeKey);
            }
        }

        private static int ComputeLevelByCapacity(ClientCrowdInfoAntennaDTO antenna, int activeConnections)
        {
            var cap = antenna.MaxCapacity <= 0 ? 1 : antenna.MaxCapacity;
            var ratio = (double)activeConnections / cap;

            if (ratio >= 0.90) return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical;
            if (ratio >= 0.70) return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.High;
            if (ratio >= 0.40) return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Medium;
            return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Low;
        }

        private async Task SafeLoadAntennasAsync()
        {
            try
            {
                var data = await CrowdInfoAntennaService.GetAllAsync();
                _allAntennas = data?.ToList() ?? new();
            }
            catch
            {
                _allAntennas = new();
            }
        }

        private async Task<List<ClientGptInteractionDTO>> SafeGetGptAsync()
        {
            try
            {
                return (await GptInteractionService.GetAllInteractions())?.ToList() ?? new();
            }
            catch
            {
                return new();
            }
        }

        private async Task SendUserPromptAsync()
        {
            var prompt = _userPrompt?.Trim();
            if (string.IsNullOrWhiteSpace(prompt) || _isSendingPrompt || _disposed)
                return;

            _isSendingPrompt = true;

            try
            {
                var result = await GptClientOrchestrator.RunAsync(
                    prompt,
                    latitude: DefaultCenter.lat,
                    longitude: DefaultCenter.lng,
                    preferAsyncPipeline: true);

                Console.WriteLine($"[HOME GPT] Started={result.Started}, InteractionId={result.InteractionId}, RequestId={result.RequestId}");

                _userPrompt = string.Empty;
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME GPT] SendUserPromptAsync failed: {ex}");
            }
            finally
            {
                _isSendingPrompt = false;
            }
        }

        private Task OnGptInteractionUpdatedAsync(ClientGptInteractionDTO dto)
        {
            ApplyOrInsertGptInteraction(dto);
            return InvokeAsync(StateHasChanged);
        }

        private Task OnGptStatusChangedAsync(string message)
        {
            _gptStatusMessage = message;
            return InvokeAsync(StateHasChanged);
        }

        private void ApplyOrInsertGptInteraction(ClientGptInteractionDTO dto)
        {
            if (dto is null || dto.Id <= 0)
                return;

            Upsert(_all, dto);
            Upsert(_visible, dto);

            GptInteractions = _all
                .OrderByDescending(x => x.CreatedAt)
                .ToList();

            if (_visible.Count > MaxVisibleGptItems)
                _visible.RemoveRange(MaxVisibleGptItems, _visible.Count - MaxVisibleGptItems);
        }

        private static void Upsert(List<ClientGptInteractionDTO> list, ClientGptInteractionDTO dto)
        {
            var idx = list.FindIndex(x => x.Id == dto.Id);
            if (idx >= 0)
                list[idx] = dto;
            else
                list.Insert(0, dto);
        }

        private void LoadMore()
        {
            var next = _all.Skip(_currentIndex).Take(PageSize).ToList();
            _visible.AddRange(next);
            _currentIndex += next.Count;
        }

        private void ToggleHistory()
        {
            _historyCollapsed = !_historyCollapsed;
        }

        private IEnumerable<ClientGptInteractionDTO> FilterGpt(IEnumerable<ClientGptInteractionDTO> src)
        {
            var q = _q?.Trim();
            if (string.IsNullOrWhiteSpace(q)) return src;

            return src.Where(x =>
                (!string.IsNullOrEmpty(x.Prompt) && x.Prompt.Contains(q, StringComparison.OrdinalIgnoreCase)) ||
                (!string.IsNullOrEmpty(x.Response) && x.Response.Contains(q, StringComparison.OrdinalIgnoreCase)));
        }

        private async Task Replay(int id)
        {
            try
            {
                await GptInteractionService.ReplayInteraction(id);
            }
            catch
            {
            }
        }

        private async Task Copy(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            await JS.InvokeVoidAsync("navigator.clipboard.writeText", text);
        }

        private static string Shorten(string s, int max)
            => string.IsNullOrWhiteSpace(s) ? "" : (s.Length <= max ? s : s[..max] + "…");

        private async Task ScrollToSuggestions()
            => await JS.InvokeVoidAsync("OutZen.scrollIntoViewById", "suggestions",
                new { behavior = "smooth", block = "start" });

        protected Task EnableSoundAsync() => Task.CompletedTask;

        private async Task ToggleDrawer()
        {
            var now = Environment.TickCount64;
            if (now - _lastToggleMs < 200) return;
            _lastToggleMs = now;

            drawerOpen = !drawerOpen;
            _dragWired = false;
            await InvokeAsync(StateHasChanged);
        }

        public async Task SendMessageAsync()
        {
            if (IsSending) return;
            if (string.IsNullOrWhiteSpace(NewMessage)) return;

            IsSending = true;
            try
            {
                await MessageService.PostAsync(NewMessage);
                NewMessage = "";
            }
            finally
            {
                IsSending = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private static bool IsNowActive(ClientCrowdInfoCalendarDTO x, DateTime utcNow)
        {
            if (!x.Active) return false;
            if (!double.IsFinite(x.Latitude) || !double.IsFinite(x.Longitude)) return false;
            if (x.Latitude == 0 && x.Longitude == 0) return false;

            return x.DateUtc.Date == utcNow.Date;
        }

        private MarkupString FormatGptResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (MarkupString)"<span class='gpt-empty'>— Response vide —</span>";

            var safe = System.Net.WebUtility.HtmlEncode(text.Trim());

            var parts = Regex.Split(
                safe,
                @"(?<=[\.!\?])\s+(?=[A-ZÀÂÄÇÉÈÊËÎÏÔÖÙÛÜ])")
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (parts.Count <= 1)
                return (MarkupString)$"<p>{safe}</p>";

            var html = string.Join("", parts.Select(p => $"<p>{p.Trim()}</p>"));
            return (MarkupString)html;
        }

        public sealed class MessageFormModel
        {
            [Required]
            [MinLength(2)]
            public string NewMessage { get; set; } = string.Empty;
        }
    }
}






















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
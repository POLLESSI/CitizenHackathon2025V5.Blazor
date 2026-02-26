// Pages/Index.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
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
        // -----------------------------
        // Inject
        // -----------------------------
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
        [Inject] public GptInteractionService GptService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        // -----------------------------
        // Map settings (Base)
        // -----------------------------
        protected override string ScopeKey => "home";
        protected override string MapId => "leafletMap-home";

        protected override bool EnableHybrid => true;
        protected override bool EnableCluster => true;
        protected override bool EnableWeatherLegend => true;
        protected override int DefaultZoom => 12;
        protected override int HybridThreshold => 13;

        // -----------------------------
        // UI / Data
        // -----------------------------
        public MessageFormModel Model { get; } = new();
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

        // Antenna overlay
        private List<ClientCrowdInfoAntennaDTO> _allAntennas = new();
        private readonly ConcurrentDictionary<int, ClientAntennaCountsDTO> _countsByAntenna = new();
        private readonly ConcurrentQueue<(int AntennaId, ClientAntennaCountsDTO Counts)> _pendingCountsUntilMap = new();

        // DotNetRef (map click callbacks)
        private DotNetObjectReference<Index> _dotNetRef;

        // Hubs / timers
        private HubConnection _antennaHub;
        private HubConnection _gptHub;

        private PeriodicTimer _timer;
        private bool _timerStarted;

        // GPT drawer minimal state (keep what you want)
        private long _lastToggleMs;
        private bool _dragWired;
        private bool drawerOpen;
        private string _userPrompt = "";

        // -----------------------------
        // GPT list (search + pagination)
        // -----------------------------
        private string _q = "";
        private readonly List<ClientGptInteractionDTO> _all = new();
        private readonly List<ClientGptInteractionDTO> _visible = new();
        private int _currentIndex = 0;
        private const int PageSize = 20;
        private int VisibleCount => _visible.Count;
        private bool CanLoadMore => _currentIndex < _all.Count;

        // -----------------------------
        // JSInvokable (map -> Blazor)
        // -----------------------------
        [JSInvokable]
        public Task SelectSuggestionFromMap(int suggestionId)
        {
            Console.WriteLine($"[Map] Suggestion clicked: {suggestionId}");
            return Task.CompletedTask;
        }

        // -----------------------------
        // Lifecycle
        // -----------------------------
        protected override async Task OnInitializedAsync()
        {
            // 1) kick async loads in parallel
            var trafficTask = TrafficConditionService.GetLatestTrafficConditionAsync();
            var crowdTask = CrowdInfoService.GetLatestCrowdInfoNonNullAsync();
            var eventTask = EventService.GetLatestEventAsync();
            var suggestionTask = SuggestionService.GetLatestSuggestionAsync();
            var placeTask = PlaceService.GetLatestPlaceAsync();
            var weatherTask = WeatherForecastService.GetLatestWeatherForecastAsync();

            // GPT interactions (not blocking the rest)
            Task<List<ClientGptInteractionDTO>> gptTask = SafeGetGptAsync();

            await Task.WhenAll(trafficTask, crowdTask, eventTask, suggestionTask, placeTask, weatherTask, gptTask);

            _traffic = trafficTask.Result ?? new();
            _crowds = crowdTask.Result ?? new();
            _events = (eventTask.Result ?? Enumerable.Empty<ClientEventDTO>()).ToList();
            _suggestions = (suggestionTask.Result ?? Enumerable.Empty<ClientSuggestionDTO>()).ToList();
            _places = (placeTask.Result ?? Enumerable.Empty<ClientPlaceDTO>()).ToList();
            _weather = (weatherTask.Result ?? Enumerable.Empty<ClientWeatherForecastDTO>()).ToList();
            GptInteractions = gptTask.Result ?? new();
            // Populate drawer list
            _all.Clear();
            _all.AddRange(GptInteractions.OrderByDescending(x => x.CreatedAt)); // optional
            _visible.Clear();
            _currentIndex = 0;
            LoadMore();

            // 2) coords filtering (Belgium bounds)
            static bool HasValidCoord(double lat, double lng)
                => lat is >= 49.45 and <= 51.6 && lng is >= 2.3 and <= 6.6;

            _places = _places.Where(p => HasValidCoord(p.Latitude, p.Longitude)).ToList();
            _events = _events.Where(e => HasValidCoord(e.Latitude, e.Longitude)).ToList();
            _crowds = _crowds.Where(c => HasValidCoord(c.Latitude, c.Longitude)).ToList();

            // 3) calendar list
            _allCal = (await CrowdInfoCalendarService.GetAllSafeAsync()).ToList();

            // 4) antennas list (if you need it for overlay) – optional but useful
            await SafeLoadAntennasAsync();

            // 5) start antenna hub (counts updates)
            await EnsureAntennaHubAsync();

            // 6) periodic calendar refresh
            StartCalendarTimer();

            // 7) Render + trigger boot/seed via base
            await InvokeAsync(StateHasChanged);
            await NotifyDataLoadedAsync(fit: true);
        }

        // -----------------------------
        // Base hooks
        // -----------------------------
        protected override async Task SeedAsync(bool fit)
        {
            // Seed = push bundles + calendar markers
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
            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", _allCal, ScopeKey);

            if (fit)
            {
                try { await JS.InvokeVoidAsync("OutZenInterop.fitToBundles", ScopeKey, new { maxZoom = 16 }); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.activateHybridAndZoom", ScopeKey, HybridThreshold); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey); } catch { }
            }
        }

        protected override async Task OnMapReadyAsync()
        {
            // Map is booted here. Only hooks + flush pending.
            _dotNetRef ??= DotNetObjectReference.Create(this);
            try { await JS.InvokeVoidAsync("OutZenInterop.registerDotNetRef", ScopeKey, _dotNetRef); } catch { }

            // Option: show all antennas as neutral markers first
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

            // Flush pending antenna updates that arrived before map boot
            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                try { await ApplyAntennaCriticalOverlayAsync(item.AntennaId, item.Counts); } catch { }
            }

            // optional: ensure hybrid refresh after container is stable
            try { await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey); } catch { }
        }

        protected override async Task OnBeforeDisposeAsync()
        {
            _timerStarted = false;
            try { _timer?.Dispose(); } catch { }

            try { if (_antennaHub is not null) await _antennaHub.DisposeAsync(); } catch { }
            try { if (_gptHub is not null) await _gptHub.DisposeAsync(); } catch { }

            try { await JS.InvokeVoidAsync("OutZenInterop.unregisterDotNetRef", ScopeKey); } catch { }
            try { _dotNetRef?.Dispose(); } catch { }
            _dotNetRef = null;
        }

        // -----------------------------
        // Timer: calendar active markers
        // -----------------------------
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

            var now = DateTime.UtcNow;
            var active = _allCal.Where(x => IsNowActive(x, now)).ToList();

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", active, ScopeKey);

            var activeIds = active.Select(x => $"cc:{x.Id}").ToList();
            await JS.InvokeVoidAsync("OutZenInterop.pruneCrowdCalendarMarkers", activeIds, ScopeKey);
        }

        // -----------------------------
        // SignalR: Antenna hub
        // -----------------------------
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

        // -----------------------------
        // Overlay antenna critical only
        // -----------------------------
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

        // -----------------------------
        // GPT Hub + prompt
        // -----------------------------
        private async Task EnsureGptHubAsync()
        {
            if (_gptHub is not null) return;

            var url = Navigation.ToAbsoluteUri(GptInteractionHubMethods.HubPath).ToString();

            _gptHub = new HubConnectionBuilder()
                .WithUrl(url, opt =>
                {
                    opt.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? "";
                    opt.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
                })
                .WithAutomaticReconnect()
                .Build();

            await _gptHub.StartAsync();
        }

        private async Task SendUserPromptAsync()
        {
            var prompt = _userPrompt?.Trim();
            if (string.IsNullOrWhiteSpace(prompt)) return;

            await EnsureGptHubAsync();
            await _gptHub!.InvokeAsync(GptInteractionHubMethods.FromClient.RefreshGpt, prompt);
            _userPrompt = "";
        }
        private void LoadMore()
        {
            var next = _all.Skip(_currentIndex).Take(PageSize).ToList();
            _visible.AddRange(next);
            _currentIndex += next.Count;
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
                // You have @inject GptInteractionService GptService in the Razor.
                // Two options :
                // 1) Use your service injected into the code-behind (recommended) :
                await GptInteractionService.ReplayInteraction(id);

                // 2) If you absolutely want to use the Razor's GptService :
                // => It also needs to be injected into the code-behind (see below).
            }
            catch
            {
                // optional toast/log
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

        protected Task EnableSoundAsync()
        {
            // future JS hook or user settings
            return Task.CompletedTask;
        }
        private async Task ToggleDrawer()
        {
            var now = Environment.TickCount64;
            if (now - _lastToggleMs < 200) return;
            _lastToggleMs = now;

            drawerOpen = !drawerOpen;
            _dragWired = false;
            await InvokeAsync(StateHasChanged);
        }

        // -----------------------------
        // Messages
        // -----------------------------
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

        // -----------------------------
        // Calendar active helper
        // -----------------------------
        private static bool IsNowActive(ClientCrowdInfoCalendarDTO x, DateTime utcNow)
        {
            if (!x.Active) return false;
            if (!double.IsFinite(x.Latitude) || !double.IsFinite(x.Longitude)) return false;
            if (x.Latitude == 0 && x.Longitude == 0) return false;

            var startUtc = x.DateUtc.AddHours(-Math.Max(0, x.LeadHours));

            TimeSpan duration = TimeSpan.FromHours(6);
            if (x.StartLocalTime.HasValue && x.EndLocalTime.HasValue)
            {
                var d = x.EndLocalTime.Value - x.StartLocalTime.Value;
                if (d > TimeSpan.Zero && d < TimeSpan.FromHours(48)) duration = d;
            }

            var endUtc = x.DateUtc.Add(duration);
            return utcNow >= startUtc && utcNow <= endUtc;
        }

        // -----------------------------
        // Form model
        // -----------------------------
        public sealed class MessageFormModel
        {
            [Required]
            [MinLength(2)]
            public string NewMessage { get; set; } = string.Empty;
        }
    }
}






















































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
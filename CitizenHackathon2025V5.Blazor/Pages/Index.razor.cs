using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;

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
        [Inject] public CrowdSafetyAlertClientService CrowdSafetyAlertService { get; set; } = default!;
        [Inject] public IDisasterCriticalAlertClientService DisasterCriticalAlertService { get; set; } = default!;

        [Inject] public EventService EventService { get; set; } = default!;
        [Inject] public SuggestionService SuggestionService { get; set; } = default!;
        [Inject] public PlaceService PlaceService { get; set; } = default!;
        [Inject] public WeatherForecastService WeatherForecastService { get; set; } = default!;
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public IGptClientOrchestrator GptClientOrchestrator { get; set; } = default!;
        [Inject] public ITrafficCriticalAlertClientService TrafficCriticalAlertService { get; set; } = default!;
        [Inject] public IWeatherCriticalAlertClientService WeatherCriticalAlertService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        protected override string ScopeKey => "home";
        protected override string MapId => "leafletMap-home";

        protected override bool EnableHybrid => true;
        protected override bool EnableCluster => false;
        protected override bool EnableWeatherLegend => true;
        protected override int DefaultZoom => 12;
        protected override int HybridThreshold => 13;
        protected override bool ForceBootOnFirstRender => false;
        protected override bool ResetMarkersOnBoot => false;
        private bool _criticalDisasterSending;
        private string _criticalDisasterStatus;

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
        private bool _dragWired;
        private bool drawerOpen;
        private bool _historyCollapsed = true;
        private bool _criticalAlertSending;
        private bool _criticalWeatherAlertSending;
        private string _criticalWeatherAlertStatus;
        private bool _criticalTrafficSending;
        private string _criticalTrafficStatus;
        private bool CanLoadMore => _currentIndex < _all.Count;

        private int _currentIndex = 0;
        private int VisibleCount => _visible.Count;
        private int? _selectedPlaceId;
        private long _lastToggleMs;
        private double _selectedLatitude;
        private double _selectedLongitude;

        private string _selectedPlaceName = "Current location";
        private string _userPrompt = "";
        private string _gptStatusMessage;
        private string _criticalAlertStatus;
        private string _q = "";
       
        private const int PageSize = 20;
        private const int MaxVisibleGptItems = 30;

        private const double DevFallbackLatitude = 50.380000;
        private const double DevFallbackLongitude = 4.682000;
        private const string DevFallbackPlaceName = "Bambois";

        private readonly List<ClientGptInteractionDTO> _all = new();
        private readonly List<ClientGptInteractionDTO> _visible = new();
        private readonly SemaphoreSlim _homeRefreshLock = new(1, 1);

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
            _crowds = _crowds.Where(c => HasValidCoord(c.Latitude, c.Longitude)).Where(c => !IsStaleManualCriticalAlert(c)).ToList();

            await ResolveNearestPlaceFromUserLocationAsync();

            _allCal = (await CrowdInfoCalendarService.GetAllSafeAsync()).ToList();

            await SafeLoadAntennasAsync();
            await EnsureAntennaHubAsync();
            await LoadLatestCrowdSafetyAlertsAsync();

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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (firstRender)
            {
                await JS.InvokeVoidAsync("OutZenInterop.makeAlertClusterDraggable");
            }
        }
        protected override async Task SeedAsync(bool fit)
        {
            var payload = new
            {
                events = _events,
                places = Array.Empty<object>(),
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

            Console.WriteLine($"[HOME][Calendar] Seeded calendar markers: {todayCalendar.Count}");

            if (fit)
            {
                //try { await JS.InvokeVoidAsync("OutZenInterop.fitToBundles", ScopeKey, new { maxZoom = 16 }); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.fitToMarkers", ScopeKey, new { maxZoom = 16 }); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.activateHybridAndZoom", ScopeKey, HybridThreshold); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey); } catch { }
            }
        }

        private async Task SendCriticalCrowdAlertAsync()
        {
            await ResolveNearestPlaceFromUserLocationAsync();
            if (_selectedPlaceId is null or <= 0)
            {
                await ResolveNearestPlaceFromUserLocationAsync();
            }

            if (_selectedPlaceId is null or <= 0)
            {
                ToastService.ShowWarning("No nearby place could be resolved from your location.");
                return;
            }

            _criticalAlertSending = true;

            try
            {
                Console.WriteLine($"[ALERT] Selected={_selectedPlaceName} ({_selectedPlaceId})");

                var result = await CriticalAlertService.SendCriticalAlertAsync(
                     _selectedPlaceId.Value,
                     $"Manual critical alert for {_selectedPlaceName}");

                if (result.Ok)
                {
                    ToastService.ShowError(
                        $"CRITICAL CROWD ALERT sent for {_selectedPlaceName}.",
                        settings =>
                        {
                            settings.Timeout = 0;
                            settings.ShowProgressBar = true;
                        });

                    _criticalAlertStatus = $"Critical alert active for {_selectedPlaceName}";

                    var declaredAtUtc = DateTime.UtcNow;
                    var expiresAtUtc = declaredAtUtc.AddMinutes(5);

                    await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateFullAlertMarker",
                        new
                        {
                            PlaceId = _selectedPlaceId.Value,
                            PlaceName = _selectedPlaceName,
                            Latitude = _selectedLatitude,
                            Longitude = _selectedLongitude,
                            DeclaredAtUtc = declaredAtUtc,
                            ExpiresAtUtc = expiresAtUtc,
                            kind = "crowd",
                            title = "🚨 FULL ALERT",
                            description = $"Critical crowd alert declared at {_selectedPlaceName}",
                            icon = "🚨"
                        },
                        ScopeKey);

                    await RefreshHomeDataAsync(fit: false);
                }

                if (!result.Ok)
                {
                    Console.Error.WriteLine(result.Error);
                    ToastService.ShowWarning("Alert could not be sent. Check browser/API console.");
                    return;
                }

                if (result.Status == "Pending")
                {
                    _criticalAlertStatus =
                        $"Signalement reçu pour {_selectedPlaceName}. Confirmation {result.ConfirmationCount}/{result.RequiredCount}.";
                    ToastService.ShowInfo(_criticalAlertStatus);
                    return;
                }

                if (result.Status == "Confirmed")
                {
                    ToastService.ShowError(
                        $"CRITICAL CROWD ALERT confirmed for {_selectedPlaceName}.",
                        settings =>
                        {
                            settings.Timeout = 0;
                            settings.ShowProgressBar = true;
                        });

                    _criticalAlertStatus = $"Critical alert confirmed for {_selectedPlaceName}";

                    var declaredAtUtc = DateTime.UtcNow;

                    await JS.InvokeVoidAsync(
                        "OutZenInterop.addOrUpdateFullAlertMarker",
                        new
                        {
                            PlaceId = _selectedPlaceId.Value,
                            PlaceName = _selectedPlaceName,
                            Latitude = _selectedLatitude,
                            Longitude = _selectedLongitude,
                            DeclaredAtUtc = declaredAtUtc,
                            ExpiresAtUtc = result.ExpiresAtUtc ?? declaredAtUtc.AddMinutes(5),
                            kind = "crowd",
                            title = "🚨 FULL ALERT",
                            description = $"Confirmed critical crowd alert at {_selectedPlaceName}",
                            icon = "🚨"
                        },
                        ScopeKey);

                    await RefreshHomeDataAsync(fit: false);
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

        private async Task SendCriticalWeatherAlertAsync()
        {
            try
            {
                _criticalWeatherAlertSending = true;

                await ResolveNearestPlaceFromUserLocationAsync();

                if (_selectedLatitude == 0 || _selectedLongitude == 0)
                {
                    ToastService.ShowWarning("Unable to resolve location for weather alert.");
                    return;
                }

                var result = await WeatherCriticalAlertService.SendCriticalWeatherAlertAsync(
                    latitude: (decimal)_selectedLatitude,
                    longitude: (decimal)_selectedLongitude,
                    weatherType: WeatherType.Thunderstorm,
                    description: $"Manual critical weather alert near {_selectedPlaceName}");

                if (result.Ok)
                {
                    _criticalWeatherAlertStatus = $"Critical weather alert confirmed for {_selectedPlaceName}";

                    ToastService.ShowWarning("⛈️ CRITICAL WEATHER ALERT SENT");

                    var declaredAtUtc = DateTime.UtcNow;

                    var expiresAtUtc = result.ExpiresAtUtc ?? declaredAtUtc.AddMinutes(5);

                    await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateWeatherAlertMarker",
                        new
                        {
                            PlaceId = _selectedPlaceId.Value,
                            PlaceName = _selectedPlaceName,
                            Latitude = _selectedLatitude,
                            Longitude = _selectedLongitude,
                            DeclaredAtUtc = declaredAtUtc,
                            ExpiresAtUtc = expiresAtUtc,
                            kind = "weather",
                            title = "⚠️ WEATHER ALERT",
                            description = $"Critical weather alert declared at {_selectedPlaceName}",
                            icon = "⛈️"
                        },
                        ScopeKey);
                }
                else
                {
                    _criticalWeatherAlertStatus = result.Error ?? "Unknown weather alert error.";

                    ToastService.ShowError(_criticalWeatherAlertStatus);
                }
            }
            catch (Exception ex)
            {
                _criticalWeatherAlertStatus = ex.Message;

                ToastService.ShowError(ex.Message);
            }
            finally
            {
                _criticalWeatherAlertSending = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task SendCriticalTrafficAlertAsync()
        {
            _criticalTrafficSending = true;

            try
            {
                await ResolveNearestPlaceFromUserLocationAsync();

                if (_selectedLatitude == 0 || _selectedLongitude == 0)
                {
                    _criticalTrafficStatus = "Unable to resolve GPS location for traffic alert.";
                    ToastService.ShowWarning(_criticalTrafficStatus);
                    return;
                }

                var placeId = _selectedPlaceId ?? 0;
                var placeName = string.IsNullOrWhiteSpace(_selectedPlaceName)
                    ? "Current location"
                    : _selectedPlaceName;

                var result = await TrafficCriticalAlertService.SendCriticalTrafficAlertAsync(
                    latitude: (decimal)_selectedLatitude,
                    longitude: (decimal)_selectedLongitude,
                    trafficLevel: CitizenHackathon2025.Contracts.Enums.TrafficLevel.Jammed,
                    description: $"Manual critical traffic congestion alert near {placeName}");

                if (!result.Ok)
                {
                    _criticalTrafficStatus = result.Error ?? "Unknown traffic alert error.";
                    ToastService.ShowError(_criticalTrafficStatus);
                    return;
                }

                var declaredAtUtc = DateTime.UtcNow;
                var expiresAtUtc = result.ExpiresAtUtc ?? declaredAtUtc.AddMinutes(5);

                _criticalTrafficStatus = $"Critical traffic congestion confirmed for {placeName}";

                ToastService.ShowWarning(
                    $"🚗 CRITICAL TRAFFIC CONGESTION confirmed for {placeName}.",
                    settings =>
                    {
                        settings.Timeout = 0;
                        settings.ShowProgressBar = true;
                    });

                await JS.InvokeVoidAsync(
                    "OutZenInterop.addOrUpdateTrafficAlertMarker",
                    new
                    {
                        PlaceId = placeId,
                        PlaceName = placeName,
                        Latitude = _selectedLatitude,
                        Longitude = _selectedLongitude,
                        DeclaredAtUtc = declaredAtUtc,
                        ExpiresAtUtc = expiresAtUtc,
                        kind = "traffic",
                        title = "🚗 CRITICAL TRAFFIC",
                        description = $"Critical traffic congestion declared near {placeName}",
                        icon = "🚗",
                        severity = "Jammed",
                        trafficLevel = "Jammed"
                    },
                    ScopeKey);

                await RefreshHomeDataAsync(fit: false);
            }
            finally
            {
                _criticalTrafficSending = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task SendCriticalDisasterAlertAsync()
        {
            _criticalDisasterSending = true;

            try
            {
                await ResolveNearestPlaceFromUserLocationAsync();

                var placeName = string.IsNullOrWhiteSpace(_selectedPlaceName)
                    ? "Current location"
                    : _selectedPlaceName;

                var result = await DisasterCriticalAlertService.SendCriticalDisasterAlertAsync(
                    latitude: (decimal)_selectedLatitude,
                    longitude: (decimal)_selectedLongitude,
                    placeName: placeName,
                    disasterType: DisasterType.MassCasualty,
                    description: $"Manual disaster alert near {placeName}");

                if (!result.Ok)
                {
                    _criticalDisasterStatus = result.Error ?? "Disaster alert failed.";
                    ToastService.ShowError(_criticalDisasterStatus);
                    return;
                }

                if (result.Status == "Pending")
                {
                    _criticalDisasterStatus =
                        $"Disaster alert pending: {result.ConfirmationCount}/{result.RequiredCount} confirmations.";

                    ToastService.ShowWarning(_criticalDisasterStatus);
                    return;
                }

                _criticalDisasterStatus =
                    $"DISASTER ALERT confirmed for {placeName}. Emergency escalation simulated.";

                ToastService.ShowError(
                    $"🚨 DISASTER ALERT confirmed for {placeName}. Simulation: emergency escalation request created.",
                    settings =>
                    {
                        settings.Timeout = 0;
                        settings.ShowProgressBar = true;
                    });

                await JS.InvokeVoidAsync(
                    "OutZenInterop.addOrUpdateDisasterAlertMarker",
                    new
                    {
                        PlaceName = placeName,
                        Latitude = _selectedLatitude,
                        Longitude = _selectedLongitude,
                        DeclaredAtUtc = DateTime.UtcNow,
                        ExpiresAtUtc = result.ExpiresAtUtc ?? DateTime.UtcNow.AddMinutes(10),
                        title = "🚨 DISASTER ALERT",
                        description = "Simulation only - pending operator review for emergency escalation.",
                        icon = "🚨",
                        severity = "Critical"
                    },
                    ScopeKey);
            }
            finally
            {
                _criticalDisasterSending = false;
                await InvokeAsync(StateHasChanged);
            }
        }
        private async Task RefreshHomeDataAsync(bool fit = false)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            if (!await _homeRefreshLock.WaitAsync(0))
                return;

            try
            {
                var trafficTask = TrafficConditionService.GetLatestTrafficConditionAsync();
                var crowdTask = CrowdInfoService.GetLatestCrowdInfoNonNullAsync();
                var eventTask = EventService.GetLatestEventAsync();
                var suggestionTask = SuggestionService.GetLatestSuggestionAsync();
                var weatherTask = WeatherForecastService.GetLatestWeatherForecastAsync();

                await Task.WhenAll(
                    trafficTask,
                    crowdTask,
                    eventTask,
                    suggestionTask,
                    weatherTask
                );

                static bool HasValidCoord(double lat, double lng)
                    => lat is >= 49.45 and <= 51.6 && lng is >= 2.3 and <= 6.6;

                _traffic = trafficTask.Result ?? new();
                _crowds = (crowdTask.Result ?? new())
                    .Where(c => HasValidCoord(c.Latitude, c.Longitude))
                    .Where(c => !IsStaleManualCriticalAlert(c))
                    .ToList();

                _events = (eventTask.Result ?? Enumerable.Empty<ClientEventDTO>())
                    .Where(e => HasValidCoord(e.Latitude, e.Longitude))
                    .ToList();

                _suggestions = (suggestionTask.Result ?? Enumerable.Empty<ClientSuggestionDTO>())
                    .ToList();

                _weather = (weatherTask.Result ?? Enumerable.Empty<ClientWeatherForecastDTO>())
                    .ToList();

                await SeedAsync(fit);

                await LoadLatestCrowdSafetyAlertsAsync();

                try { await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey); } catch { }

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME] RefreshHomeDataAsync failed: {ex}");
            }
            finally
            {
                _homeRefreshLock.Release();
            }
        }

        protected override async Task OnMapReadyAsync()
        {
            _dotNetRef ??= DotNetObjectReference.Create(this);
            try { await JS.InvokeVoidAsync("OutZenInterop.registerDotNetRef", ScopeKey, _dotNetRef); } catch { }

            //foreach (var a in _allAntennas)
            //{
            //    await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker", new
            //    {
            //        Id = a.Id,
            //        Latitude = a.Latitude,
            //        Longitude = a.Longitude,
            //        Name = a.Name,
            //        Description = "Waiting for counts…"
            //    }, ScopeKey);
            //}

            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                try { await ApplyAntennaCriticalOverlayAsync(item.AntennaId, item.Counts); } catch { }
            }

            await LoadLatestCrowdSafetyAlertsAsync();

            try { await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey); } catch { }

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
            _timer ??= new PeriodicTimer(TimeSpan.FromSeconds(15));
            _timerStarted = true;

            _ = Task.Run(async () =>
            {
                while (_timerStarted && await _timer.WaitForNextTickAsync())
                {
                    try
                    {
                        await InvokeAsync(async () =>
                        {
                            await RefreshCalendarMarkersNowAsync();
                            await RefreshHomeDataAsync(fit: false);
                        });
                    }
                    catch { }
                }
            });
        }

        private async Task RefreshCalendarMarkersNowAsync()
        {
            if (!IsMapBooted)
                return;

            var nowUtc = DateTime.UtcNow;
            var active = _allCal
                .Where(x => IsNowActive(x, nowUtc))
                .ToList();

            if (active.Count == 0)
            {
                Console.WriteLine("[HOME][Calendar] No active calendar markers. Skip prune.");
                return;
            }

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", active, ScopeKey);

            var activeIds = active
                .Select(x => $"cc:{x.Id}")
                .ToList();

            if (activeIds.Count > 0)
            {
                await JS.InvokeVoidAsync("OutZenInterop.pruneCrowdCalendarMarkers", activeIds, ScopeKey);
            }
        }

        private async Task EnsureAntennaHubAsync()
        {
            try
            {
                if (_antennaHub is not null &&
                    _antennaHub.State == HubConnectionState.Connected)
                {
                    return;
                }

                var token = await Auth.GetAccessTokenAsync();

                if (string.IsNullOrWhiteSpace(token))
                {
                    Console.Error.WriteLine("[HOME] Antenna hub skipped: no JWT token.");
                    return;
                }

                _antennaHub = new HubConnectionBuilder()
                    .WithUrl(
                        HubUrls.Build(CrowdInfoAntennaConnectionHubMethods.HubPath),
                        options =>
                        {
                            options.Transports =
                                HttpTransportType.WebSockets |
                                HttpTransportType.ServerSentEvents;

                            options.AccessTokenProvider = () =>
                            {
                                return Task.FromResult<string>(token);
                            };
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

                await JoinCriticalAntennaGroupsAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"[HOME] Antenna hub unavailable. Map rendering continues without realtime antenna updates. {ex}");
            }
        }

        private async Task JoinCriticalAntennaGroupsAsync()
        {
            if (_antennaHub is null || _antennaHub.State != HubConnectionState.Connected)
                return;

            var ids = _allAntennas
                .Select(a => a.Id)
                .Distinct()
                .Take(100)
                .ToArray();

            if (ids.Length == 0)
                return;

            try
            {
                await _antennaHub.InvokeAsync(
                    CrowdInfoAntennaConnectionHubMethods.FromClient.JoinAntennas,
                    ids);

                Console.WriteLine($"[HOME][AntennaHub] Joined {ids.Length} antenna groups.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME][AntennaHub] JoinAntennas failed: {ex.Message}");
            }
        }

        private async Task ApplyAntennaCriticalOverlayAsync(int antennaId, ClientAntennaCountsDTO counts)
        {
            var antenna = _allAntennas.FirstOrDefault(a => a.Id == antennaId);
            if (antenna is null) return;

            var level = ComputeLevelByCapacity(antenna, counts.ActiveConnections);
            var key = $"ant:{antennaId}";

            if (level == (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical)
            {
                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaAlertCircle", new
                {
                    AntennaId = antennaId,
                    Latitude = antenna.Latitude,
                    Longitude = antenna.Longitude,
                    Title = "Concentration critique détectée",
                    Message = $"Concentration critique détectée près de {antenna.Name}.",
                    Severity = 4,
                    ActiveConnections = counts.ActiveConnections,
                    UniqueDevices = counts.UniqueDevices,
                    Status = "Realtime"
                }
                , ScopeKey);
            }
            else
            {
                await JS.InvokeVoidAsync(
                    "OutZenInterop.removeAntennaAlertCircle",
                    antennaId,
                    ScopeKey);
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
                    languageCode: "fr-FR");

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

        private static string BuildCrowdMarkerKey(int placeId)
            => $"crowd-place:{placeId}";
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

        private async Task ResolveNearestPlaceFromUserLocationAsync()
        {
            try
            {
                if (_places.Count == 0)
                {
                    _selectedPlaceId = null;
                    _selectedPlaceName = "No place available";
                    return;
                }

                var pos = await JS.InvokeAsync<ClientUserPositionDto>(
                    "outzenLocation.getCurrentPosition");
                Console.WriteLine($"[GPS] {pos.Latitude}, {pos.Longitude}");

                Console.WriteLine($"[HOME] GPS resolved: {pos.Latitude}, {pos.Longitude}");

                var nearestPlaces = _places
                    .Where(p =>
                        double.IsFinite(p.Latitude) &&
                        double.IsFinite(p.Longitude) &&
                        p.Latitude != 0 &&
                        p.Longitude != 0)
                    .Select(p => new
                    {
                        Place = p,
                        Distance = GetDistanceKm(
                            pos.Latitude,
                            pos.Longitude,
                            p.Latitude,
                            p.Longitude)
                    })
                    .OrderBy(x => x.Distance)
                    .Take(10)
                    .ToList();
                //.OrderBy(p =>
                //    GetDistanceKm(
                //        pos.Latitude,
                //        pos.Longitude,
                //        p.Latitude,
                //        p.Longitude))
                //.FirstOrDefault();
                foreach (var p in nearestPlaces)
                {
                    Console.WriteLine(
                        $"[DIST] {p.Place.Name} => {p.Distance:F2} km");
                }

                var nearest = nearestPlaces.FirstOrDefault();

                if (nearest is null)
                {
                    _selectedPlaceId = null;
                    _selectedPlaceName = "No nearby place found";
                    return;
                }

                _selectedPlaceId = nearest.Place.Id;
                _selectedPlaceName = nearest.Place.Name ?? $"Place #{nearest.Place.Id}";

                _selectedLatitude = nearest.Place.Latitude;
                _selectedLongitude = nearest.Place.Longitude;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME] Geolocation failed: {ex.Message}");

                _selectedPlaceId = null;
                _selectedPlaceName = "Location unavailable";

                ToastService.ShowWarning("Unable to get your current location.");
                var nearest = _places
                    .OrderBy(p =>
                        GetDistanceKm(
                            DevFallbackLatitude,
                            DevFallbackLongitude,
                            p.Latitude,
                            p.Longitude))
                    .FirstOrDefault();

                            if (nearest != null)
                            {
                                _selectedPlaceId = nearest.Id;
                                _selectedPlaceName = nearest.Name;
                                _selectedLatitude = nearest.Latitude;
                                _selectedLongitude = nearest.Longitude;
                }
            }
        }

        private async Task LoadLatestCrowdSafetyAlertsAsync()
        {
            if (_disposed)
                return;

            if (!IsMapBooted)
                return;

            try
            {
                var alerts = await CrowdSafetyAlertService.GetLatestAsync(50);

                var activeCriticalAlerts = alerts
                    .Where(a => a.Active)
                    .Where(a => a.Status == "PendingValidation" || a.Status == "Validated")
                    .Where(a => a.Severity >= 3)
                    .ToList();

                foreach (var alert in activeCriticalAlerts)
                {
                    await ApplyCrowdSafetyAlertMarkerAsync(alert);
                }

                Console.WriteLine($"[HOME][CrowdSafety] Loaded {activeCriticalAlerts.Count} active safety alerts.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME][CrowdSafety] LoadLatestCrowdSafetyAlertsAsync failed: {ex}");
            }
        }

        private async Task ApplyCrowdSafetyAlertMarkerAsync(ClientCrowdSafetyAlertDTO alert)
        {
            if (_disposed)
                return;

            if (!IsMapBooted)
                return;

            var lat = (double)alert.Latitude;
            var lng = (double)alert.Longitude;

            if (!double.IsFinite(lat) || !double.IsFinite(lng))
                return;

            if (lat is < 49.45 or > 51.6 || lng is < 2.3 or > 6.6)
                return;

            var icon = alert.Severity switch
            {
                >= 4 => "🚨",
                3 => "⚠️",
                _ => "📡"
            };

            var description =
                $"{alert.Message}<br/>" +
                $"Active connections : {alert.ActiveConnections}<br/>" +
                $"Unique devices : {alert.UniqueDevices}<br/>" +
                $"Status : {alert.Status}";

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaAlertCircle",

                new
                {
                    AntennaId = alert.AntennaId,
                    Latitude = lat,
                    Longitude = lng,
                    Title = alert.Title,
                    Message = alert.Message,
                    Severity = alert.Severity,
                    ActiveConnections = alert.ActiveConnections,
                    UniqueDevices = alert.UniqueDevices,
                    Status = alert.Status
                },
                ScopeKey);
        }
        private static double GetDistanceKm(double lat1, double lon1, double lat2, double lon2)
        {
            const double earthRadiusKm = 6371.0;

            static double ToRad(double degrees) => degrees * Math.PI / 180.0;

            var dLat = ToRad(lat2 - lat1);
            var dLon = ToRad(lon2 - lon1);

            var a =
                Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRad(lat1)) *
                Math.Cos(ToRad(lat2)) *
                Math.Sin(dLon / 2) *
                Math.Sin(dLon / 2);

            var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

            return earthRadiusKm * c;
        }

        private static bool IsNowActive(ClientCrowdInfoCalendarDTO x, DateTime utcNow)
        {
            if (!x.Active) return false;
            if (!double.IsFinite(x.Latitude) || !double.IsFinite(x.Longitude)) return false;
            if (x.Latitude == 0 && x.Longitude == 0) return false;

            var belgiumTz = TimeZoneInfo.FindSystemTimeZoneById("Europe/Brussels");
            var localToday = TimeZoneInfo.ConvertTimeFromUtc(utcNow, belgiumTz).Date;

            return x.DateUtc.Date == localToday;
        }

        private static bool IsStaleManualCriticalAlert(ClientCrowdInfoDTO c)
        {
            var nowUtc = DateTime.UtcNow;

            var timestampUtc = c.Timestamp.Kind == DateTimeKind.Utc
                ? c.Timestamp
                : c.Timestamp.ToUniversalTime();

            var isOldCriticalCrowd =
                c.CrowdLevel >= 4 &&
                timestampUtc < nowUtc.AddMinutes(-5);

            return isOldCriticalCrowd;
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
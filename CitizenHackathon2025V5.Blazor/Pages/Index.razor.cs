using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using DevExpress.CodeParser;
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
        [Inject] public TrafficConditionService TrafficConditionService { get; set; } = default!;
        [Inject] public CrowdInfoService CrowdInfoService { get; set; } = default!;
        [Inject] public CrowdInfoCalendarService CrowdInfoCalendarService { get; set; } = default!;
        [Inject] public CrowdSafetyAlertClientService CrowdSafetyAlertService { get; set; } = default!;
        [Inject] public EventService EventService { get; set; } = default!;
        [Inject] public SuggestionService SuggestionService { get; set; } = default!;
        [Inject] public PlaceService PlaceService { get; set; } = default!;
        [Inject] public WeatherForecastService WeatherForecastService { get; set; } = default!;
        [Inject] public GptInteractionService GptInteractionService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public ICrowdInfoAntennaService CrowdInfoAntennaService { get; set; } = default!;
        [Inject] public IDisasterCriticalAlertClientService DisasterCriticalAlertService { get; set; } = default!;
        [Inject] public IHubTokenService HubTokenService { get; set; } = default!;
        [Inject] public IGptClientOrchestrator GptClientOrchestrator { get; set; } = default!;
        [Inject] public ITrafficCriticalAlertClientService TrafficCriticalAlertService { get; set; } = default!;
        [Inject] public IWeatherCriticalAlertClientService WeatherCriticalAlertService { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;
        [Inject] public EmergencyAlertStateService EmergencyAlertState { get; set; } = default!;
        //[Inject] public IAuthService Auth { get; set; } = default!;

        protected override string ScopeKey => "home";
        protected override string MapId => "leafletMap-home";

        protected override bool EnableHybrid => true;
        protected override bool EnableCluster => false;
        protected override bool EnableWeatherLegend => true;
        protected override int DefaultZoom => 12;
        protected override int HybridThreshold => 14;
        protected override bool ForceBootOnFirstRender => false;
        protected override bool ResetMarkersOnBoot => false;
        private bool _criticalDisasterSending;

        private bool _msgDrawerOpen = false;
        private bool _gptDrawerOpen = false;

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
        private bool _criticalTrafficSending;
        private string _criticalWeatherAlertStatus;
        private string _criticalTrafficStatus;
        private bool _emergencyStateHandlerAttached;
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
        private const int FullAlertMinimumDistinctDevices = 4;

        private const double DevFallbackLatitude = 50.380000;
        private const double DevFallbackLongitude = 4.682000;
        private const string DevFallbackPlaceName = "Bambois";

        private readonly List<ClientGptInteractionDTO> _all = new();
        private readonly List<ClientGptInteractionDTO> _visible = new();
        private readonly SemaphoreSlim _homeRefreshLock = new(1, 1);
        private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];
        private readonly List<HomeEmergencyNotice> _homeEmergencyNotices = new();

        private readonly Dictionary<string, CancellationTokenSource> _homeEmergencyNoticeTimers = new();
        private readonly HashSet<string> _seenHomeEmergencyNotices = new(StringComparer.Ordinal);

        private const int MaxVisibleEmergencyNotices = 3;

        [JSInvokable]
        public Task SelectSuggestionFromMap(int suggestionId)
        {
            Console.WriteLine($"[Map] Suggestion clicked: {suggestionId}");
            return Task.CompletedTask;
        }

        protected override async Task OnInitializedAsync()
        {
            Console.WriteLine($"[HOME][{_instanceId}] " + "OnInitializedAsync started.");

            GptClientOrchestrator.InteractionUpdated += OnGptInteractionUpdatedAsync;
            GptClientOrchestrator.StatusChanged += OnGptStatusChangedAsync;

            /*
             * Emergency Intelligence is initialized
             * early because safety information takes
             * priority over ordinary Home content.
             */

            await InitializeHomeEmergencyIntelligenceAsync();

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

            Console.WriteLine($"[HOME][{_instanceId}] " + "Initial data loaded.");

            await NotifyDataLoadedAsync(fit: true);
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            await base.OnAfterRenderAsync(firstRender);

            if (!firstRender)
                return;

            try
            {
                await JS.InvokeVoidAsync("OutZenInterop.makeAlertClusterDraggable");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME] Alert cluster wiring failed: {ex.Message}");
            }
        }
        protected override async Task SeedAsync(bool fit)
        {
            Console.WriteLine(
                $"[HOME][Seed] fit={fit}, " +
                $"events={_events.Count}, " +
                $"places={_places.Count}, " +
                $"crowds={_crowds.Count}, " +
                $"traffic={_traffic.Count}, " +
                $"weather={_weather.Count}, " +
                $"suggestions={_suggestions.Count}");
            var payload = new
            {
                events = _events,

                // Use _places only if you wish
                // display them in the homepage areas.
                places = _places,

                crowds = _crowds,
                suggestions = _suggestions,
                traffic = _traffic,
                weather = _weather,

                gpt = Array.Empty<object>()
            };

            // 0 = adaptive tolerance based on the zoom level.
            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateBundleMarkers", payload, 0, ScopeKey);

            var nowUtc = DateTime.UtcNow;
            var todayCalendar = _allCal.Where(x => IsNowActive(x, nowUtc)).ToList();

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", todayCalendar, ScopeKey);

            if (fit)
            {
                try
                {
                    await JS.InvokeVoidAsync("OutZenInterop.fitToAllMarkers", ScopeKey, new { maxZoom = 16 });
                }
                catch
                {
                    // Loading the map must not fail.
                    // solely because of a zoom adjustment.
                }

                try
                {
                    await JS.InvokeVoidAsync("OutZenInterop.activateHybridAndZoom", ScopeKey, HybridThreshold);
                }
                catch
                {
                }

                try
                {
                    await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey);
                }
                catch
                {
                }
            }
        }

        private async Task InitializeHomeEmergencyIntelligenceAsync()
        {
            if (!_emergencyStateHandlerAttached)
            {
                /*
                 * Live event:
                 * temporary BE-Alert popup only.
                 */
                EmergencyAlertState.LiveAlertReceived += OnHomeLiveEmergencyAlertReceivedAsync;

                /*
                 * Authoritative state:
                 * persistent critical map marker.
                 *
                 * Also receives cancellation / expiry /
                 * REST reconciliation changes.
                 */
                EmergencyAlertState.StateChanged += OnHomeEmergencyStateChangedAsync;

                _emergencyStateHandlerAttached = true;
            }

            try
            {
                await EmergencyAlertState.StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME][EMERGENCY] " + $"startup failed: {ex}");
            }

            /*
             * If the visitor arrives after a BE-Alert
             * was already published, show at most
             * the two latest active messages.
             */
            foreach (
                var alert
                in EmergencyAlertState
                    .Snapshot
                    .Where(ShouldShowBeAlertNotice)
                    .OrderByDescending(x => x.LastUpdatedAtUtc)
                    .Take(2)
                    .Reverse())
            {
                ShowHomeEmergencyNotice(alert);
            }
        }
        private async Task OnHomeLiveEmergencyAlertReceivedAsync(EmergencyAlertSignalRDTO alert)
        {
            if (!ShouldShowBeAlertNotice(alert))
            {
                return;
            }


            await InvokeAsync(
                () =>
                {
                    ShowHomeEmergencyNotice(alert);
                });
        }
        private async Task SendCriticalCrowdAlertAsync()
        {
            if (_criticalAlertSending)
                return;

            _criticalAlertSending = true;

            await InvokeAsync(StateHasChanged);

            try
            {
                await ResolveNearestPlaceFromUserLocationAsync();

                if (_selectedPlaceId is null or <= 0)
                {
                    await ResolveNearestPlaceFromUserLocationAsync();
                }

                if (_selectedPlaceId is null or <= 0)
                {
                    ToastService.ShowWarning("No nearby place could be resolved " + "from your location.");

                    return;
                }

                Console.WriteLine($"[ALERT] Selected=" + $"{_selectedPlaceName} " + $"({_selectedPlaceId})");

                var result = await CriticalAlertService.SendCriticalAlertAsync(_selectedPlaceId.Value, $"Manual critical alert for " + $"{_selectedPlaceName}");

                Console.WriteLine(
                    "[ALERT RESULT] " +
                    $"Ok={result.Ok}, " +
                    $"Status={result.Status}, " +
                    $"ConfirmationCount=" +
                    $"{result.ConfirmationCount}, " +
                    $"RequiredCount=" +
                    $"{result.RequiredCount}, " +
                    $"ExpiresAtUtc=" +
                    $"{result.ExpiresAtUtc}, " +
                    $"Error={result.Error}");

                if (!result.Ok)
                {
                    Console.Error.WriteLine(result.Error);

                    ToastService.ShowWarning("Alert could not be sent. " + "Check browser/API console.");

                    return;
                }

                var requiredConfirmations = Math.Max(FullAlertMinimumDistinctDevices, result.RequiredCount);
                var isConfirmed = string.Equals(result.Status, "Confirmed", StringComparison.OrdinalIgnoreCase)
                    &&
                    result.ConfirmationCount >= requiredConfirmations;

                if (!isConfirmed)
                {
                    _criticalAlertStatus =
                        $"Signalement reçu pour " +
                        $"{_selectedPlaceName}. " +
                        $"Confirmation " +
                        $"{result.ConfirmationCount}/" +
                        $"{requiredConfirmations}.";

                    ToastService.ShowInfo(_criticalAlertStatus);

                    /*
                     * No prior Full Alert marker
                     * confirmation by the minimum number
                     * of distinct devices.
                     */
                    return;
                }

                _criticalAlertStatus = $"Critical alert confirmed for " + $"{_selectedPlaceName}";

                ToastService.ShowError($"CRITICAL CROWD ALERT confirmed for " + $"{_selectedPlaceName}.",
                    settings =>
                    {
                        settings.Timeout = 0;
                        settings.ShowProgressBar = true;
                    });

                var declaredAtUtc = DateTime.UtcNow;

                var expiresAtUtc = result.ExpiresAtUtc ?? declaredAtUtc.AddMinutes(5);

                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateFullAlertMarker",
                    new
                    {
                        PlaceId = _selectedPlaceId.Value,
                        PlaceName = _selectedPlaceName,
                        Latitude = _selectedLatitude,
                        Longitude = _selectedLongitude,
                        DeclaredAtUtc = declaredAtUtc,
                        ExpiresAtUtc = expiresAtUtc,
                        Status = "Confirmed",

                        ConfirmationCount = result.ConfirmationCount,

                        RequiredCount = requiredConfirmations,

                        Source = "ControlCenter",
                        kind = "crowd",
                        title = "🚨 FULL ALERT",
                        description = $"Confirmed critical crowd alert " + $"at {_selectedPlaceName}",
                        icon = "🚨"
                    },
                    ScopeKey);

                await RefreshHomeDataAsync(fit: false);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[ALERT] SendCriticalCrowdAlertAsync " + $"failed: {ex}");

                _criticalAlertStatus = $"Critical alert error: {ex.Message}";

                ToastService.ShowError(_criticalAlertStatus);
            }
            finally
            {
                _criticalAlertSending = false;

                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task SendCriticalWeatherAlertAsync()
        {
            if (_criticalWeatherAlertSending)
                return;

            _criticalWeatherAlertSending = true;

            await InvokeAsync(StateHasChanged);

            try
            {
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
            if (_criticalTrafficSending)
                return;

            _criticalTrafficSending = true;
            await InvokeAsync(StateHasChanged);

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

                ToastService.ShowWarning($"🚗 CRITICAL TRAFFIC CONGESTION confirmed for {placeName}.",
                    settings =>
                    {
                        settings.Timeout = 0;
                        settings.ShowProgressBar = true;
                    });

                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateTrafficAlertMarker",
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
            if (_criticalDisasterSending)
                return;

            _criticalDisasterSending = true;
            await InvokeAsync(StateHasChanged);

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
                    _criticalDisasterStatus = $"Disaster alert pending: {result.ConfirmationCount}/{result.RequiredCount} confirmations.";

                    ToastService.ShowWarning(_criticalDisasterStatus);
                    return;
                }

                _criticalDisasterStatus = $"DISASTER ALERT confirmed for {placeName}. Emergency escalation simulated.";

                ToastService.ShowError($"🚨 DISASTER ALERT confirmed for {placeName}. Simulation: emergency escalation request created.",
                    settings =>
                    {
                        settings.Timeout = 0;
                        settings.ShowProgressBar = true;
                    });

                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateDisasterAlertMarker",
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
            if (_disposed || !IsMapBooted)
                return;

            if (!await _homeRefreshLock.WaitAsync(0))
            {
                Console.WriteLine("[HOME][Refresh] skipped: " + "another refresh is running.");

                return;
            }

            try
            {
                var trafficTask = TrafficConditionService.GetLatestTrafficConditionAsync();
                var crowdTask = CrowdInfoService.GetLatestCrowdInfoNonNullAsync();
                var eventTask = EventService.GetLatestEventAsync();
                var suggestionTask = SuggestionService.GetLatestSuggestionAsync();
                var placeTask = PlaceService.GetLatestPlaceAsync();
                var weatherTask = WeatherForecastService.GetLatestWeatherForecastAsync();
                var calendarTask = CrowdInfoCalendarService.GetAllSafeAsync();

                await Task.WhenAll(
                    trafficTask,
                    crowdTask,
                    eventTask,
                    suggestionTask,
                    placeTask,
                    weatherTask,
                    calendarTask);

                static bool HasValidCoord(double latitude, double longitude)
                {
                    return
                        latitude is >= 49.45 and <= 51.6 &&
                        longitude is >= 2.3 and <= 6.6;
                }

                var nextTraffic = trafficTask.Result?.ToList() ?? new List<ClientTrafficConditionDTO>();
                var nextCrowds = (crowdTask.Result ?? Enumerable.Empty<ClientCrowdInfoDTO>())
                    .Where(c => HasValidCoord(c.Latitude, c.Longitude))
                    .Where(c => !IsStaleManualCriticalAlert(c))
                    .ToList();

                var nextEvents = (eventTask.Result ?? Enumerable.Empty<ClientEventDTO>())
                    .Where(e => HasValidCoord(e.Latitude, e.Longitude))
                    .ToList();

                var nextSuggestions = (suggestionTask.Result ?? Enumerable.Empty<ClientSuggestionDTO>())
                    .ToList();

                var nextPlaces = (placeTask.Result ?? Enumerable.Empty<ClientPlaceDTO>())
                    .Where(p => HasValidCoord(p.Latitude,p.Longitude))
                    .ToList();

                var nextWeather = (weatherTask.Result ?? Enumerable.Empty<ClientWeatherForecastDTO>()).ToList();
                var nextCalendar = calendarTask.Result?.ToList() ?? new List<ClientCrowdInfoCalendarDTO>();


                var incomingTotal =
                    nextTraffic.Count +
                    nextCrowds.Count +
                    nextEvents.Count +
                    nextSuggestions.Count +
                    nextPlaces.Count +
                    nextWeather.Count;

                var currentTotal =
                    _traffic.Count +
                    _crowds.Count +
                    _events.Count +
                    _suggestions.Count +
                    _places.Count +
                    _weather.Count;

                Console.WriteLine(
                    "[HOME][Refresh] incoming: " +
                    $"traffic={nextTraffic.Count}, " +
                    $"crowds={nextCrowds.Count}, " +
                    $"events={nextEvents.Count}, " +
                    $"suggestions={nextSuggestions.Count}, " +
                    $"places={nextPlaces.Count}, " +
                    $"weather={nextWeather.Count}, " +
                    $"calendar={nextCalendar.Count}");

                /*
                 * Protection principale :
                 * une panne générale ne doit jamais effacer
                 * une carte déjà remplie.
                 */
                if (incomingTotal == 0 && currentTotal > 0)
                {
                    Console.Error.WriteLine(
                        "[HOME][Refresh] all endpoints " +
                        "returned empty results; " +
                        "previous map data preserved.");

                    return;
                }

                _traffic = KeepPreviousWhenEmpty(_traffic, nextTraffic, "traffic");
                _crowds = KeepPreviousWhenEmpty(_crowds, nextCrowds, "crowds");
                _events = KeepPreviousWhenEmpty(_events, nextEvents, "events");

                /*
                 * An empty list of suggestions can be
                 * perfectly normal.
                 */
                _suggestions = nextSuggestions;
                _places = KeepPreviousWhenEmpty(_places, nextPlaces,"places");
                _weather = KeepPreviousWhenEmpty(_weather, nextWeather, "weather");

                /*
                 * Do not replace an existing calendar with a
                 * empty list potentially caused by an API error.
                 */
                if (nextCalendar.Count > 0)
                {
                    _allCal = nextCalendar;

                    Console.WriteLine($"[HOME][Refresh] " + $"calendar={_allCal.Count}");
                }
                else if (_allCal.Count > 0)
                {
                    Console.WriteLine("[HOME][Refresh] " + "calendar=0; preserving " + $"{_allCal.Count} previous item(s).");
                }

                /*
                 * SeedAsync uses _allCal to select the
                 * currently active calendar markers.
                 */
                await SeedAsync(fit);
                await LoadLatestCrowdSafetyAlertsAsync();

                /*
                 * Emergency state may already have been loaded
                 * before Leaflet became ready.
                 */
                await SyncHomeEmergencyCriticalMarkersAsync();

                try
                {
                    await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey);
                }
                catch
                {
                }

                try
                {
                    await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", ScopeKey);
                }
                catch
                {
                }

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                /*
                 * The old collections remain intact,
                 * because assignments only occur
                 * after Task.WhenAll.
                 */
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

            try
            {
                await JS.InvokeVoidAsync(
                    "OutZenInterop.registerDotNetRef",
                    ScopeKey,
                    _dotNetRef);
            }
            catch
            {
            }

            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                try
                {
                    await ApplyAntennaCriticalOverlayAsync(item.AntennaId, item.Counts);
                }
                catch
                {
                }
            }


            await
                LoadLatestCrowdSafetyAlertsAsync();


            /*
             * Persistent official emergency markers.
             */
            await
                SyncHomeEmergencyCriticalMarkersAsync();


            try
            {
                await JS.InvokeVoidAsync(
                    "OutZenInterop.refreshMapSize",
                    ScopeKey);
            }
            catch
            {
            }


            try
            {
                await JS.InvokeVoidAsync(
                    "OutZenInterop.refreshHybridNow",
                    ScopeKey);
            }
            catch
            {
            }
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

            if (_emergencyStateHandlerAttached)
            {
                EmergencyAlertState.LiveAlertReceived -= OnHomeLiveEmergencyAlertReceivedAsync;

                EmergencyAlertState.StateChanged -= OnHomeEmergencyStateChangedAsync;

                _emergencyStateHandlerAttached = false;
            }

            /*
             * Stop pending automatic popup timers.
             */
            foreach (var timer in _homeEmergencyNoticeTimers.Values.ToArray())
            {
                try
                {
                    timer.Cancel();
                }
                catch
                {
                }
                timer.Dispose();
            }
            _homeEmergencyNoticeTimers.Clear();
            _homeEmergencyNotices.Clear();

            Console.WriteLine($"[HOME][{_instanceId}] " + "Disposing.");
        }

        private void StartCalendarTimer()
        {
            if (_timerStarted)
                return;

            _timerStarted = true;
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
            _ = RunHomeRefreshLoopAsync();
        }

        private async Task RunHomeRefreshLoopAsync()
        {
            var ticksUntilFullRefresh = 4;

            try
            {
                while (!_disposed && _timerStarted && _timer is not null && await _timer.WaitForNextTickAsync())
                {
                    await InvokeAsync(RefreshCalendarMarkersNowAsync);

                    ticksUntilFullRefresh--;

                    /*
                     * 4 × 15 seconds = 60 seconds.
                     */
                    if (ticksUntilFullRefresh > 0)
                        continue;

                    ticksUntilFullRefresh = 4;

                    await InvokeAsync(() => RefreshHomeDataAsync(fit: false));
                }
            }
            catch (ObjectDisposedException)
            {
                // Normal page closure.
            }
            catch (OperationCanceledException)
            {
                // Normal page closure.
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME] Refresh loop failed: {ex}");
            }
        }

        private void ShowHomeEmergencyNotice(EmergencyAlertSignalRDTO alert)
        {
            if (!ShouldShowBeAlertNotice(alert))
            {
                return;
            }

            /*
             * An Update to the same alert is a new
             * message if LastUpdatedAtUtc changed.
             */
            var key = $"{alert.Id:N}|" + $"{alert.LastUpdatedAtUtc:O}";

            if (!_seenHomeEmergencyNotices.Add(key))
            {
                return;
            }


            var critical = IsCriticalEmergencyNotice(alert);
            var title = !string.IsNullOrWhiteSpace(alert.Headline) ? alert.Headline.Trim() : "BE-Alert";
            var message = !string.IsNullOrWhiteSpace(alert.Description) ? alert.Description.Trim() : alert.Instructions?.Trim() ?? string.Empty;
            var instructions = !string.IsNullOrWhiteSpace(alert.Instructions)
                &&
                !string.Equals(alert.Instructions.Trim(), message, StringComparison.Ordinal) ? alert.Instructions.Trim() : null;

            var notice = new HomeEmergencyNotice(
                Key: key,
                AlertId: alert.Id,
                SourceCode: alert.SourceCode,
                Title: title,
                Message: message,
                Instructions: instructions,
                LastUpdatedAtUtc: alert.LastUpdatedAtUtc,
                Critical: critical,
                DurationSeconds: critical ? 25 : 18);

            /*
             * Newest first.
             */
            _homeEmergencyNotices.Insert(0, notice);

            while (_homeEmergencyNotices.Count > MaxVisibleEmergencyNotices)
            {
                var oldest = _homeEmergencyNotices[^1];
                RemoveHomeEmergencyNotice(oldest.Key);
            }


            StateHasChanged();

            StartHomeEmergencyNoticeTimer(notice);
        }

        private void StartHomeEmergencyNoticeTimer(HomeEmergencyNotice notice)
        {
            if ( _homeEmergencyNoticeTimers.Remove(notice.Key, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }

            var cts = new CancellationTokenSource();

            _homeEmergencyNoticeTimers[notice.Key] = cts;
            _ = AutoDismissHomeEmergencyNoticeAsync(notice, cts);
        }

        private async Task AutoDismissHomeEmergencyNoticeAsync(HomeEmergencyNotice notice, CancellationTokenSource cts)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(notice.DurationSeconds), cts.Token);

                await InvokeAsync(
                    () =>
                    {
                        RemoveHomeEmergencyNotice(notice.Key);

                        StateHasChanged();
                    });
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void RemoveHomeEmergencyNotice(string key)
        {
            var notice = _homeEmergencyNotices.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.Ordinal));


            if (notice is not null)
            {
                _homeEmergencyNotices.Remove(notice);
            }


            if (
                _homeEmergencyNoticeTimers.Remove(key, out var timer))
            {
                timer.Cancel();
                timer.Dispose();
            }
        }

        private void DismissHomeEmergencyNotice(string key)
        {
            RemoveHomeEmergencyNotice(key);

            StateHasChanged();
        }

        private async Task RefreshCalendarMarkersNowAsync()
        {
            if (!IsMapBooted)
                return;

            var nowUtc = DateTime.UtcNow;
            var active = _allCal.Where(x => IsNowActive(x, nowUtc)).ToList();

            if (active.Count == 0)
            {
                Console.WriteLine("[HOME][Calendar] No active calendar markers. Skip prune.");
                return;
            }

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", active, ScopeKey);

            var activeIds = active.Select(x => $"cc:{x.Id}").ToList();

            if (activeIds.Count > 0)
            {
                await JS.InvokeVoidAsync("OutZenInterop.pruneCrowdCalendarMarkers", activeIds, ScopeKey);
            }
        }

        private async Task EnsureAntennaHubAsync()
        {
            if (_disposed)
                return;

            try
            {
                if (_antennaHub is not null)
                {
                    if (_antennaHub.State is
                        HubConnectionState.Connected or
                        HubConnectionState.Connecting or
                        HubConnectionState.Reconnecting)
                    {
                        return;
                    }

                    /*
                     * An old Disconnected connection should not
                     * remain attached with its old handlers.
                     */
                    try
                    {
                        await _antennaHub.DisposeAsync();
                    }
                    catch
                    {
                        // Best effort cleanup.
                    }

                    _antennaHub = null;
                }

                var hubUrl = HubUrls.Build(CrowdInfoAntennaConnectionHubMethods.HubPath);

                _antennaHub = new HubConnectionBuilder()
                    .WithUrl(
                        hubUrl,
                        options =>
                        {
                            options.Transports =
                                HttpTransportType.WebSockets |
                                HttpTransportType.ServerSentEvents;

                            /*
                             * Important :
                             * do not capture a token only once.
                             *
                             * AccessTokenProvider will be called during
                             * SignalR reconnections and can therefore
                             * obtain a new ephemeral JWT.
                             */
                            options.AccessTokenProvider = async () =>
                                await HubTokenService.GetHubTokenAsync();

                        })
                    .WithAutomaticReconnect(
                        new[]
                        {
                            TimeSpan.Zero,
                            TimeSpan.FromSeconds(2),
                            TimeSpan.FromSeconds(5),
                            TimeSpan.FromSeconds(10),
                            TimeSpan.FromSeconds(30)
                        })
                    .Build();

                RegisterAntennaHubHandlers();

                _antennaHub.Reconnecting += error =>
                {
                    Console.WriteLine("[HOME][AntennaHub] Reconnecting. " + $"Reason={error?.Message ?? "connection lost"}");

                    return Task.CompletedTask;
                };

                _antennaHub.Reconnected += async connectionId =>
                {
                    Console.WriteLine("[HOME][AntennaHub] Reconnected. " + $"ConnectionId={connectionId ?? "<unknown>"}");

                    /*
                     * A SignalR reconnection creates a new
                     * server connection.
                     *
                     * The groups from the old connection
                     * are not preserved.
                     */
                    await JoinCriticalAntennaGroupsAsync();
                };

                _antennaHub.Closed += error =>
                {
                    Console.Error.WriteLine("[HOME][AntennaHub] Closed. " + $"Reason={error?.Message ?? "normal closure"}");

                    return Task.CompletedTask;
                };

                Console.WriteLine($"[HOME][AntennaHub] Connecting to {hubUrl}");

                await _antennaHub.StartAsync();

                Console.WriteLine("[HOME][AntennaHub] Connected. " + $"ConnectionId={_antennaHub.ConnectionId}");

                await JoinCriticalAntennaGroupsAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[HOME] Antenna hub unavailable. " + "Map rendering continues without realtime " + $"antenna updates. {ex}");
            }
        }
        private async Task JoinCriticalAntennaGroupsAsync()
        {
            if (_antennaHub is null || _antennaHub.State != HubConnectionState.Connected)
                return;

            var ids = _allAntennas.Select(a => a.Id).Distinct().Take(100).ToArray();

            if (ids.Length == 0)
                return;

            try
            {
                await _antennaHub.InvokeAsync(CrowdInfoAntennaConnectionHubMethods.FromClient.JoinAntennas, ids);

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

            var observedDevices = counts.UniqueDevices > 0 ? counts.UniqueDevices : counts.ActiveConnections;
            var level = ComputeLevelByCapacity(antenna, observedDevices);

            if (level == (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical)
            {
                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaAlertCircle", new
                {
                    AntennaId = antennaId,
                    Latitude = antenna.Latitude,
                    Longitude = antenna.Longitude,
                    Title = "Concentration critique " + "détectée par antenne",
                    Message = $"Concentration critique " + $"détectée près de " + $"{antenna.Name}.",
                    Severity = 4,
                    ActiveConnections = counts.ActiveConnections,
                    UniqueDevices = counts.UniqueDevices,
                    Status = "Realtime"
                }
                , ScopeKey);
            }
            else
            {
                await JS.InvokeVoidAsync("OutZenInterop.removeAntennaAlertCircle", antennaId, ScopeKey);
            }
        }

        private static int ComputeLevelByCapacity(ClientCrowdInfoAntennaDTO antenna, int observedDevices)
        {
            if (observedDevices <= 0)
            {
                return (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Low;
            }

            if (antenna.MaxCapacity is null or <= 0)
            {
                return observedDevices switch
                {
                    >= 200 => (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical,
                    >= 100 => (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.High,
                    >= 40 => (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Medium,
                    _ => (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Low
                };
            }

            var ratio = observedDevices / (double)antenna.MaxCapacity.Value;

            if (ratio >= 0.90)
            {
                return (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical;
            }
            if (ratio >= 0.70)
            {
                return (int) CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.High;
            }
            if (ratio >= 0.40)
            {
                return (int)
                    CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Medium;
            }

            return (int)
                CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Low;
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
            {
                return;
            }

            _isSendingPrompt = true;
            _q = string.Empty;

            try
            {
                await GptClientOrchestrator.EnsureHubAsync();

                var latitude = _selectedLatitude is >= 49.45 and <= 51.60 ? _selectedLatitude : DefaultCenter.lat;
                var longitude = _selectedLongitude is >= 2.30 and <= 6.60 ? _selectedLongitude : DefaultCenter.lng;

                _gptStatusMessage = "Sending request to Mistral...";

                await InvokeAsync(StateHasChanged);

                var started = await GptClientOrchestrator.StartAsync(prompt, latitude, longitude, "fr-FR");

                if (started is null || started.InteractionId <= 0)
                {
                    _gptStatusMessage = "The GPT request could not be started.";

                    return;
                }

                var pendingInteraction = new ClientGptInteractionDTO
                {
                    Id = started.InteractionId,
                    Prompt = prompt,
                    Response = "Generation in progress...",
                    CreatedAt = DateTime.UtcNow,
                    Active = true,
                    SourceType = "MistralLocal"
                };

                ApplyOrInsertGptInteraction(pendingInteraction);

                _gptStatusMessage = started.Message ?? $"Generation started — interaction #{started.InteractionId}.";
                _userPrompt = string.Empty;

                Console.WriteLine(
                    $"[HOME GPT] Started " +
                    $"InteractionId={started.InteractionId}, " +
                    $"RequestId={started.RequestId}, " +
                    $"Latitude={latitude}, " +
                    $"Longitude={longitude}");

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[HOME GPT] SendUserPromptAsync failed: {ex}");

                _gptStatusMessage = $"GPT error: {ex.Message}";
            }
            finally
            {
                _isSendingPrompt = false;
                await InvokeAsync(StateHasChanged);
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

            GptInteractions = _all.OrderByDescending(x => x.CreatedAt).ToList();

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

        private void RegisterAntennaHubHandlers()
        {
            if (_antennaHub is null)
                return;

            _antennaHub.On<ClientAntennaCountsUpdateDTO>(CrowdInfoAntennaConnectionHubMethods.ToClient.AntennaCountsUpdated,
                async message =>
                {
                    if (_disposed || message is null || message.AntennaId <= 0 || message.Counts is null)
                    {
                        return;
                    }

                    _countsByAntenna[message.AntennaId] = message.Counts;

                    if (!IsMapBooted)
                    {
                        _pendingCountsUntilMap.Enqueue(
                            (
                                message.AntennaId, message.Counts
                            ));

                        return;
                    }

                    await InvokeAsync(
                        async () =>
                        {
                            await ApplyAntennaCriticalOverlayAsync(message.AntennaId, message.Counts);

                            StateHasChanged();
                        });

                    if (_disposed || message is null || message.AntennaId <= 0 || message.Counts is null)
                    {
                        return;
                    }

                    Console.WriteLine(
                        "[HOME][AntennaHub] Counts received. " +
                        $"AntennaId={message.AntennaId}, " +
                        $"Active={message.Counts.ActiveConnections}, " +
                        $"Unique={message.Counts.UniqueDevices}");
                });
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
        {
            try
            {
                var found = await JS.InvokeAsync<bool>("OutZen.scrollIntoViewById", "suggestions",
                    new
                    {
                        behavior = "smooth",
                        block = "start",
                        offset = 150
                    });

                Console.WriteLine("[HOME] Scroll To Suggestions " + $"targetFound={found}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[HOME] Scroll To Suggestions failed: " + ex);
            }
        }

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

                var pos = await JS.InvokeAsync<ClientUserPositionDto>("outzenLocation.getCurrentPosition");
                Console.WriteLine($"[GPS] {pos.Latitude}, {pos.Longitude}");

                Console.WriteLine($"[HOME] GPS resolved: {pos.Latitude}, {pos.Longitude}");

                var nearestPlaces = _places
                    .Where(p => double.IsFinite(p.Latitude) && double.IsFinite(p.Longitude) && p.Latitude != 0 && p.Longitude != 0)
                    .Select(p => new
                    {
                        Place = p,
                        Distance = GetDistanceKm(pos.Latitude, pos.Longitude, p.Latitude, p.Longitude)
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
                    Console.WriteLine($"[DIST] {p.Place.Name} => {p.Distance:F2} km");
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
                    .OrderBy(p => GetDistanceKm(DevFallbackLatitude, DevFallbackLongitude, p.Latitude, p.Longitude))
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
                    .Where(a => string.Equals(a.Status, "Validated", StringComparison.OrdinalIgnoreCase))
                    .Where(a => a.Severity >= 4)
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

            var icon = alert.Severity switch { >= 4 => "🚨", 3 => "⚠️", _ => "📡" };

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

        private async Task SyncHomeEmergencyCriticalMarkersAsync()
        {
            if (
                _disposed
                ||
                !IsMapBooted)
            {
                return;
            }


            var alerts =
                EmergencyAlertState
                    .Snapshot
                    .Where(
                        RequiresImmediateHomeAttention)
                    .ToArray();


            var activeIds =
                new List<string>(
                    alerts.Length);


            foreach (
                var alert
                in alerts)
            {
                var id =
                    alert.Id.ToString(
                        "D");


                try
                {
                    var added =
                        await JS.InvokeAsync<bool>(
                            "OutZenInterop.__esm." +
                            "addOrUpdateEmergencyCriticalMarker",

                            alert,

                            ScopeKey);


                    if (added)
                    {
                        activeIds.Add(
                            id);
                    }


                    Console.WriteLine(
                        $"[HOME][EMERGENCY MARKER] " +
                        $"id={id}, " +
                        $"added={added}, " +
                        $"source={alert.SourceCode}, " +
                        $"severity={alert.Severity}, " +
                        $"urgency={alert.Urgency}");
                }
                catch (JSException ex)
                {
                    Console.Error.WriteLine(
                        $"[HOME][EMERGENCY MARKER] " +
                        $"upsert failed " +
                        $"id={id}: {ex.Message}");
                }
            }


            try
            {
                var removed =
                    await JS.InvokeAsync<int>(
                        "OutZenInterop.__esm." +
                        "pruneEmergencyCriticalMarkers",

                        activeIds,

                        ScopeKey);


                Console.WriteLine(
                    $"[HOME][EMERGENCY MARKER] " +
                    $"active={activeIds.Count}, " +
                    $"removed={removed}");
            }
            catch (JSException ex)
            {
                Console.Error.WriteLine(
                    $"[HOME][EMERGENCY MARKER] " +
                    $"prune failed: {ex.Message}");
            }
        }

        private async Task OnHomeEmergencyStateChangedAsync()
        {
            await InvokeAsync(
                async () =>
                {
                    if (
                        !_disposed && IsMapBooted)
                    {
                        await SyncHomeEmergencyCriticalMarkersAsync();
                    }
                    StateHasChanged();
                });
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
            var timestampUtc = c.Timestamp.Kind == DateTimeKind.Utc ? c.Timestamp : c.Timestamp.ToUniversalTime();
            var isOldCriticalCrowd = c.CrowdLevel >= 4 && timestampUtc < nowUtc.AddMinutes(-5);

            return isOldCriticalCrowd;
        }

        private static bool RequiresImmediateHomeAttention(EmergencyAlertSignalRDTO alert)
        {
            if (!alert.IsOfficial)
                return false;


            var severity = Convert.ToInt32(alert.Severity);
            var urgency = Convert.ToInt32(alert.Urgency);

            /*
             * Critical Home treatment:
             *
             * Extreme+
             *
             * OR
             *
             * Severe + Immediate.
             */
            return severity >= 4 ||
                (
                    severity >= 3 && urgency >= 4
                );
        }
        private static bool ShouldShowBeAlertNotice(EmergencyAlertSignalRDTO alert)
        {
            if (!alert.IsOfficial)
                return false;

            if (!string.Equals(alert.SourceCode, "BE-ALERT", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            /*
             * A BE-Alert public message is useful even
             * without map geometry.
             */
            return
                !string.IsNullOrWhiteSpace(alert.Headline)
                || !string.IsNullOrWhiteSpace(alert.Description)
                || !string.IsNullOrWhiteSpace(alert.Instructions);
        }

        private static bool IsCriticalEmergencyNotice(EmergencyAlertSignalRDTO alert)
        {
            if (!alert.IsOfficial)
                return false;

            var severity = Convert.ToInt32(alert.Severity);

            var urgency = Convert.ToInt32(alert.Urgency);

            return
                severity >= 4 ||
                (
                    severity >= 3 && urgency >= 4
                );
        }

        private static List<T> KeepPreviousWhenEmpty<T>(List<T> current, List<T> incoming, string category)
        {
            if (incoming.Count > 0)
                return incoming;

            if (current.Count == 0)
                return incoming;

            Console.WriteLine($"[HOME][Refresh] " + $"{category}=0; preserving " + $"{current.Count} previous item(s).");

            return current;
        }
        private MarkupString FormatGptResponse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return (MarkupString)"<span class='gpt-empty'>— Response vide —</span>";

            var safe = System.Net.WebUtility.HtmlEncode(text.Trim());

            var parts = Regex.Split(safe, @"(?<=[\.!\?])\s+(?=[A-ZÀÂÄÇÉÈÊËÎÏÔÖÙÛÜ])").Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

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

        private sealed record HomeEmergencyNotice(string Key, Guid AlertId, string SourceCode, string Title, string Message, string Instructions, DateTimeOffset LastUpdatedAtUtc, bool Critical, int DurationSeconds);
    }
}









    












































































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
// Pages/Index.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.OutZens;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Utils.OutZen;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.ComponentModel.DataAnnotations;
//Index: scopeKey = "main", mapId = "outzenMap_main"

namespace CitizenHackathon2025V5.Blazor.Client.Pages
{
    public partial class Index 
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
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        // Data
        private List<ClientTrafficConditionDTO> TrafficConditionsList = new();
        private List<ClientCrowdInfoDTO> CrowdInfos = new();
        private List<ClientEventDTO> Events = new();
        private List<ClientSuggestionDTO> Suggestions = new();
        private List<ClientPlaceDTO> Places = new();
        private List<ClientWeatherForecastDTO> WeatherPoints = new();
        private List<ClientCrowdInfoCalendarDTO> _allCal = new();
        private HubConnection _hub;
        protected List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();
        public MessageFormModel Model { get; } = new();
        private const string _homeMapId = "leafletMap-home";    /*"outzenMap_main"*/
        private PeriodicTimer _timer;
        protected string NewMessage { get; set; } = string.Empty;
        protected override string ScopeKey => "home";
        protected override string MapId => "leafletMap-home";
        protected override bool EnableWeatherLegend => true;
        protected override int DefaultZoom => 12;

        private readonly ConcurrentDictionary<int, ClientAntennaCountsDTO> _countsByAntenna = new();
        private readonly ConcurrentQueue<(int AntennaId, ClientAntennaCountsDTO Counts)> _pendingCountsUntilMap = new();
        // JS
        private List<ClientCrowdInfoAntennaDTO> _allAntennas = new();
        private IJSObjectReference _leafletModule;
        private IJSObjectReference _outzen;
        private IJSObjectReference _mod;
        private bool _bundlePushed;
        private bool _timerStarted;
        private bool _dataLoaded;
        private bool _mapBooted;
        private BootResult _boot;
        private string _bootToken;

        private bool _booted;

        protected bool IsSending { get; set; }

        //private const string LeafletModulePath = "/js/app/leafletOutZen.module.js";

        private async Task ScrollToSuggestions()
            => await JS.InvokeVoidAsync("OutZen.scrollIntoViewById", "suggestions",
                new { behavior = "smooth", block = "start" });

        protected override async Task OnInitializedAsync()
        {
            var trafficTask = TrafficConditionService.GetLatestTrafficConditionAsync();
            var crowdTask = CrowdInfoService.GetLatestCrowdInfoNonNullAsync();
            var eventTask = EventService.GetLatestEventAsync();
            var suggestionTask = SuggestionService.GetLatestSuggestionAsync();
            var placeTask = PlaceService.GetLatestPlaceAsync();
            var weatherTask = WeatherForecastService.GetLatestWeatherForecastAsync();
            var gptTask = GptInteractionService.GetAllInteractions();

            await Task.WhenAll(trafficTask, crowdTask, eventTask, suggestionTask, placeTask, weatherTask, gptTask);

            TrafficConditionsList = trafficTask.Result ?? new();
            CrowdInfos = crowdTask.Result ?? new();
            Events = (eventTask.Result ?? Enumerable.Empty<ClientEventDTO>()).ToList();
            Suggestions = (suggestionTask.Result ?? Enumerable.Empty<ClientSuggestionDTO>()).ToList();
            Places = (placeTask.Result ?? Enumerable.Empty<ClientPlaceDTO>()).ToList();
            WeatherPoints = (weatherTask.Result ?? Enumerable.Empty<ClientWeatherForecastDTO>()).ToList();
            GptInteractions = (gptTask.Result ?? Enumerable.Empty<ClientGptInteractionDTO>()).ToList();

            _dataLoaded = true;
            
            try
            {
                var data = await GptInteractionService.GetAllInteractions(); // Adapt if your service exposes a different name
                if (data is not null) GptInteractions = data.ToList();
            }
            catch
            {
                // You can log in/toast if you want, but avoid breaking the page.
                GptInteractions = new();
            }

            _allCal = (await CrowdInfoCalendarService.GetAllSafeAsync()).ToList();

            _timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            _timerStarted = true;

            _ = Task.Run(async () =>
            {
                while (_timerStarted && await _timer.WaitForNextTickAsync())
                    await InvokeAsync(RefreshCalendarMarkersNowAsync);
            });

            //_hub.On<ClientAntennaCountsUpdateDTO>(
            //    CrowdInfoAntennaConnectionHubMethods.ToClient.AntennaCountsUpdated,
            //    async msg =>
            //    {
            //        _countsByAntenna[msg.AntennaId] = msg.Counts;

            //        if (!_booted || _outzen is null)
            //            _pendingCountsUntilMap.Enqueue((msg.AntennaId, msg.Counts));
            //        else
            //            await ApplyAntennaCriticalOverlayAsync(msg.AntennaId, msg.Counts);
            //    });
            
            await EnsureAntennaHubAsync();

            await InvokeAsync(StateHasChanged);
        }

        private async Task EnsureAntennaHubAsync()
        {
            if (_hub is not null) return;

            var hubUrl = HubUrls.Build(HubPaths.AntennaConnection);

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                    options.Transports = HttpTransportType.WebSockets | HttpTransportType.ServerSentEvents;
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ClientAntennaCountsUpdateDTO>(
                CrowdInfoAntennaConnectionHubMethods.ToClient.AntennaCountsUpdated,
                async msg =>
                {
                    _countsByAntenna[msg.AntennaId] = msg.Counts;

                    if (!_mapBooted)
                        _pendingCountsUntilMap.Enqueue((msg.AntennaId, msg.Counts));
                    else
                        await ApplyAntennaCriticalOverlayAsync(msg.AntennaId, msg.Counts);
                });

            await _hub.StartAsync();
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            if (_mapBooted) return;
            _mapBooted = true;

            var uri = Navigation.ToBaseRelativePath(Navigation.Uri);
            if (!string.IsNullOrWhiteSpace(uri)) return;

            Console.WriteLine($"[Index] baseRel='{uri}' full='{Navigation.Uri}' path='{new Uri(Navigation.Uri).AbsolutePath}'");

            // 1) Boot map only once
            BootResult boot = null;
            try
            {
                boot = await JS.InvokeAsync<BootResult>("OutZenInterop.bootOutZen", new
                {
                    mapId = _homeMapId,
                    scopeKey = "home",
                    center = new[] { 50.45, 4.6 },
                    zoom = 12,
                    force = false,
                    enableChart = false,
                    enableWeatherLegend = true,
                    resetMarkers = true
                });

                if (boot is null || !boot.Ok) { _mapBooted = false; return; }

                _boot = boot;
                _bootToken = boot.Token;
                _mapBooted = true;
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"[Index] JS boot failed: {jsex.Message}");
                _mapBooted = false;
                return;
            }

            

            Console.WriteLine($"[Index] boot ok={boot.Ok} mapId={boot.MapId} scope={boot.ScopeKey} token={boot.Token}");

            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                await ApplyAntennaCriticalOverlayAsync(item.AntennaId, item.Counts);
            }

            if (!_mapBooted) return;

            // 2) Push bundles as soon as dataLoaded becomes true (and only once).
            if (_dataLoaded && !_bundlePushed)
            {
                try { await PushBundlesOnceAsync(); }
                catch (Exception ex) { Console.Error.WriteLine($"[Index] ❌ PushBundlesOnceAsync failed: {ex}"); }
            }
        }

        //protected override async Task OnMapReadyAsync()
        //{
        //    // Example: load your global “payload bundles”
        //    // var payload = await BuildMainPayloadAsync();
        //    // await MapInterop.UpsertBundlesAsync(payload, tolMeters: 80, scopeKey: ScopeKey);
        //    // await MapInterop.FitToBundlesAsync(ScopeKey);
        //}
        protected override async Task OnBeforeDisposeAsync()
        {
            _timerStarted = false;
            try { _timer?.Dispose(); } catch { }

            try { if (_hub is not null) await _hub.DisposeAsync(); } catch { }

            // If you want to force a specific mapId layout
            try 
            {
                await JS.InvokeVoidAsync("OutZenInterop.disposeOutZen", new
                {
                    mapId = _homeMapId,
                    scopeKey = "home",
                    token = _bootToken
                });

            }
            catch { }
        }

        private async Task PushBundlesOnceAsync()
        {
            Console.WriteLine($"[Index] PushBundlesOnceAsync ENTER dataLoaded={_dataLoaded} bundlePushed={_bundlePushed}");
            if (!_dataLoaded || _bundlePushed) return;

            _bundlePushed = true; // ✅ reserve immediately

            try
            {
                var payload = new
                {
                    events = Events,
                    places = Places,
                    crowds = CrowdInfos,
                    suggestions = Suggestions,
                    traffic = TrafficConditionsList,
                    weather = WeatherPoints,
                    gpt = GptInteractions
                };

                var ok = await JS.InvokeAsync<bool>("OutZenInterop.addOrUpdateBundleMarkers", payload, 80, "home");
                if (!ok) { _bundlePushed = false; return; }

                await JS.InvokeAsync<bool>("OutZenInterop.enableHybridZoom", true, 13, "home");

                // Fit (NOT animated on the JS side)
                await JS.InvokeVoidAsync("OutZenInterop.fitToBundles", 30, "home");

                // Apply hybrid visibility after the view is stable
                await JS.InvokeVoidAsync("OutZenInterop.refreshHybridNow", "home");

                //await JS.InvokeVoidAsync("OutZen.ensure");
                //await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateWeatherMarkers", WeatherPoints, "home");

                //await JS.InvokeVoidAsync("OutZenInterop.forceDetailsMode", "home");

            }
            catch (Exception ex)
            {
                _bundlePushed = false; // rollback
                Console.Error.WriteLine($"[Index] PushBundlesOnceAsync FAIL: {ex}");
                throw;
            }
        }
        private async Task RefreshCalendarMarkersNowAsync()
        {
            if (!_mapBooted) return; // map not yet ready

            var now = DateTime.UtcNow;
            var active = _allCal.Where(x => IsNowActive(x, now)).ToList();

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", active);

            var activeIds = active.Select(x => $"cc:{x.Id}").ToList();
            await JS.InvokeVoidAsync("OutZenInterop.pruneCrowdCalendarMarkers", activeIds);
        }

        // ---- Actions ----
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

        private async Task ApplyAntennaCriticalOverlayAsync(int antennaId, ClientAntennaCountsDTO counts)
        {
            var antenna = _allAntennas.FirstOrDefault(a => a.Id == antennaId);
            if (antenna is null) return;

            // Calculate your level (1..4)
            var level = ComputeLevelByCapacity(antenna, counts.ActiveConnections);

            // ✅ Only critical
            var markerId = $"ant:{antennaId}";

            if (level == (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical)
            {
                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker",
                    markerId,
                    antenna.Latitude,
                    antenna.Longitude,
                    level,
                    new
                    {
                        title = antenna.Name ?? $"Antenne {antennaId}",
                        description = $"{counts.ActiveConnections} connexions • {counts.UniqueDevices} devices"
                    });
            }
            else
            {
                await JS.InvokeVoidAsync("OutZenInterop.removeAntennaMarker", markerId);
            }
        }

        private static int ComputeLevelByCapacity(ClientCrowdInfoAntennaDTO antenna, int activeConnections)
        {
            // Suppose your DTO has a capacity (e.g., MaxCapacity)
            var cap = antenna.MaxCapacity <= 0 ? 1 : antenna.MaxCapacity;

            var ratio = (double)activeConnections / cap;

            // Ex: 0-40% Low, 40-70% Medium, 70-90% High, 90%+ Critical
            if (ratio >= 0.90) return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Critical;
            if (ratio >= 0.70) return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.High;
            if (ratio >= 0.40) return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Medium;
            return (int)CitizenHackathon2025V5.Blazor.Client.Enums.CrowdLevelEnum.Low;
        }

        // ---- Helpers ----
        private static string Shorten(string s, int max)
            => string.IsNullOrWhiteSpace(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
        protected Task EnableSoundAsync()
        {
            // future JS hook or user settings
            return Task.CompletedTask;
        }

        static bool IsNowActive(ClientCrowdInfoCalendarDTO x, DateTime utcNow)
        {
            if (!x.Active) return false;
            if (!double.IsFinite(x.Latitude) || !double.IsFinite(x.Longitude)) return false;
            if (x.Latitude == 0 && x.Longitude == 0) return false;

            // Start = DateUtc - LeadHours
            var startUtc = x.DateUtc.AddHours(-Math.Max(0, x.LeadHours));

            // End = DateUtc + duration
            // duration from Start/EndLocalTime if available, else 6h
            TimeSpan duration = TimeSpan.FromHours(6);
            if (x.StartLocalTime.HasValue && x.EndLocalTime.HasValue)
            {
                var d = x.EndLocalTime.Value - x.StartLocalTime.Value;
                if (d > TimeSpan.Zero && d < TimeSpan.FromHours(48)) duration = d;
            }
            var endUtc = x.DateUtc.Add(duration);

            return utcNow >= startUtc && utcNow <= endUtc;
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
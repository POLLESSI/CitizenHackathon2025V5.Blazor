using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Utils.OutZen;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastView : IAsyncDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public WeatherForecastService WeatherForecastService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private const string _mapId = $"leafletMap-weatherforecastview";

        private IJSObjectReference _outZen;

        public List<ClientWeatherForecastDTO> WeatherForecastLists { get; set; } = new();

        private List<ClientWeatherForecastDTO> allWeatherForecasts = new();
        private List<ClientWeatherForecastDTO> visibleWeatherForecasts = new();
        private RainAlertDTO _activeRainAlert;

        private int currentIndex = 0;
        private const int PageSize = 20;

        private readonly List<ClientWeatherForecastDTO> _pendingMapUpdates = new();
        private bool _lastFirstRender;
        private bool _dataLoaded;
        private bool _mapBooted;
        private bool _initialMarkersSynced;

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        // optional (si tu l'utilises dans ton .razor)
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = await WeatherForecastService.GetAllAsync();

            Console.WriteLine($"[WF-Client] Loaded {fetched?.Count ?? 0} forecasts from API.");
            if (fetched is { Count: > 0 })
            {
                var first = fetched[0];
                Console.WriteLine($"[WF-Client] First = Id={first.Id}, Lat={first.Latitude}, Lon={first.Longitude}");
            }

            WeatherForecastLists = fetched ?? new();
            allWeatherForecasts = fetched ?? new();

            visibleWeatherForecasts.Clear();
            currentIndex = 0;
            LoadMoreItems();

            _dataLoaded = true;

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase;

            // Final URL : https://localhost:7254/hubs/weatherforecastHub
            var url = $"{apiBaseUrl}/hubs/{WeatherForecastHubMethods.HubPath}";
            Console.WriteLine($"[WF-Client] Hub URL = {url}");

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;

                    // f one day you want to force WS :
                    // options.SkipNegotiation = true;
                    // options.Transports = HttpTransportType.WebSockets;
                })
                .WithAutomaticReconnect()
                .Build();

            // === Handler: aligned with server event "ReceiveForecast" ===
            hubConnection.On<ClientWeatherForecastDTO>(WeatherForecastHubMethods.ToClient.ReceiveForecast, async dto =>
            {
                if (dto is null) return;

                Console.WriteLine($"[WF-Client] ✅ SignalR ReceiveForecast: Id={dto.Id}");

                // 1) Upsert lists
                UpsertById(WeatherForecastLists, dto);
                UpsertById(allWeatherForecasts, dto);
                UpsertVisible(dto);

                // 2) Map updates (If the map isn't ready -> queue)
                if (_outZen is null)
                {
                    _pendingMapUpdates.Add(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await AddWeatherMarkerAsync(dto);
                await _outZen.InvokeVoidAsync("fitToMarkers");

                // 3) Chart (a single source of truth)
                await UpdateChartAsync();

                await InvokeAsync(StateHasChanged);
            });

            // Archive / delete
            hubConnection.On<int>(WeatherForecastHubMethods.ToClient.EventArchived, async id =>
            {
                WeatherForecastLists.RemoveAll(c => c.Id == id);
                allWeatherForecasts.RemoveAll(c => c.Id == id);
                visibleWeatherForecasts.RemoveAll(c => c.Id == id);

                if (_outZen is not null)
                {
                    await _outZen.InvokeVoidAsync("removeCrowdMarker", WeatherMarkerId(id));
                    await _outZen.InvokeVoidAsync("fitToMarkers");
                }

                await InvokeAsync(StateHasChanged);
            });

            // SignalR custom event subscription
            WeatherHub.HeavyRainReceived += OnHeavyRainReceived;

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine("[WF-Client] hubConnection.StartAsync() OK.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[WF-Client] hubConnection.StartAsync() FAILED: {ex.Message}");
            }
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            //if (!firstRender) return;
            if (!_dataLoaded) return;
            if (_mapBooted) return;

            var exists = await JS.InvokeAsync<bool>("OutZenInterop.elementExists", _mapId);
            if (!exists) return;

            _mapBooted = true;
            //_lastFirstRender = firstRender;

            _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            var ok = await _outZen.InvokeAsync<bool>("bootOutZen", new
            {
                mapId = _mapId,
                center = new[] { 50.5, 4.7 },
                zoom = 13,
                enableChart = true,
                force = true,
                enableWeatherLegend = true,
                resetMarkers = true
            });

            //Console.WriteLine($"[WF] bootOutZen ok={ok} mapId={_mapId}");
            if (!ok) { _mapBooted = false; return; }

            Console.WriteLine($"[WF] bootOutZen ok={ok} mapId={_mapId}");

            // important if the container becomes visible after render/layout
            await _outZen.InvokeVoidAsync("refreshMapSize");
            await Task.Delay(200);
            await _outZen.InvokeVoidAsync("refreshMapSize");
            await Task.Delay(600);
            await _outZen.InvokeVoidAsync("refreshMapSize");

            // DEBUG: force a visible marker in Brussels
            await _outZen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                "wf:__debug",
                50.8503, 4.3517,
                2,
                new { title = "DEBUG", description = "Marker test", weatherType = "Clear" });

            //_mapBooted = true;

            // 2) Initial marker synchronization only once
            if (_initialMarkersSynced) return;

            _initialMarkersSynced = true;

            foreach (var dto in allWeatherForecasts)
                await AddWeatherMarkerAsync(dto);

            foreach (var dto in _pendingMapUpdates)
                await AddWeatherMarkerAsync(dto);

            _pendingMapUpdates.Clear();

            await _outZen.InvokeVoidAsync("fitToMarkers");
            await _outZen.InvokeVoidAsync("debugDumpMarkers");

            // chart init
            await UpdateChartAsync();
        }

        /// <summary>
        /// Adds or updates a marker for a forecast.
        /// </summary>
        private async Task AddWeatherMarkerAsync(ClientWeatherForecastDTO dto)
        {
            if (dto is null) return;

            Console.WriteLine($"[WF] AfterRender firstRender={_lastFirstRender} dataLoaded={_dataLoaded} mapBooted={_mapBooted}");

            Console.WriteLine($"[WF-Client] AddWeatherMarkerAsync Id={dto.Id}, Lat={dto.Latitude}, Lon={dto.Longitude}");

            if (_outZen is null) return;

            // Fallback coords (avoids 0/0)
            var lat = dto.Latitude;
            var lng = dto.Longitude;

            // fallback if latitude/longitude are invalid
            if (double.IsNaN(lat) || double.IsNaN(lng) ||
                lat is < -90 or > 90 ||
                lng is < -180 or > 180 ||
                (lat == 0 && lng == 0))
            {
                lat = 50.85;
                lng = 4.35;
            }


            Console.WriteLine($"[WF-Client] JS add marker => id={WeatherMarkerId(dto.Id)} lat={lat} lng={lng} severe={dto.IsSevere}");

            await _outZen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                WeatherMarkerId(dto.Id),
                lat,
                lng,
                dto.IsSevere ? 4 : 2,
                new
                {
                    title = dto.Summary ?? "Weather",
                    description = $"Temp: {dto.TemperatureC}°C, Vent: {dto.WindSpeedKmh} km/h (Maj {dto.DateWeather:HH:mm:ss})",
                    isTraffic = false,
                    weatherType = dto.WeatherType.ToString()
                });
            // DEBUG: How many layers are in the cluster?
            await _outZen.InvokeVoidAsync("debugClusterCount");

            Console.WriteLine($"[WF-Client] JS add marker DONE => {dto.Id}");
        }

        // ============ Helpers (clean + no dup) ============

        private static string WeatherMarkerId(int id) => $"wf:{id}";

        private static void UpsertById(List<ClientWeatherForecastDTO> list, ClientWeatherForecastDTO dto)
        {
            if (list is null || dto is null) return;

            var i = list.FindIndex(x => x.Id == dto.Id);
            if (i >= 0) list[i] = dto;
            else list.Add(dto);
        }

        private void UpsertVisible(ClientWeatherForecastDTO dto)
        {
            if (dto is null) return;

            var idx = visibleWeatherForecasts.FindIndex(x => x.Id == dto.Id);

            if (idx >= 0)
                visibleWeatherForecasts[idx] = dto;
            else
                visibleWeatherForecasts.Insert(0, dto);

            // Optional: limit to avoid a huge UI list
            // if (visibleWeatherForecasts.Count > 400) visibleWeatherForecasts = visibleWeatherForecasts.Take(400).ToList();
        }

        private void LoadMoreItems()
        {
            var next = allWeatherForecasts.Skip(currentIndex).Take(PageSize).ToList();
            visibleWeatherForecasts.AddRange(next);
            currentIndex += next.Count;
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<double>("scrollInterop.getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<double>("scrollInterop.getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<double>("scrollInterop.getClientHeight", ScrollContainerRef);

            var st = (int)Math.Truncate(scrollTop);
            var sh = (int)Math.Truncate(scrollHeight);
            var ch = (int)Math.Truncate(clientHeight);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (currentIndex < allWeatherForecasts.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private IEnumerable<ClientWeatherForecastDTO> FilterWeather(IEnumerable<ClientWeatherForecastDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (x.Summary ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                            || x.TemperatureC.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.DateWeather >= cutoff);
        }

        private async Task ChangeMetric(WeatherMetric metric)
        {
            _selectedMetric = metric;
            await UpdateChartAsync();
        }

        private async Task UpdateChartAsync()
        {
            if (_outZen is null || allWeatherForecasts.Count == 0)
                return;

            var recent = allWeatherForecasts
                .OrderByDescending(x => x.DateWeather)
                .Take(24)
                .OrderBy(x => x.DateWeather)
                .Select(x => new
                {
                    label = x.DateWeather.ToString("HH:mm"),
                    value = _selectedMetric switch
                    {
                        WeatherMetric.Temperature => x.TemperatureC,
                        WeatherMetric.Humidity => x.Humidity,
                        WeatherMetric.Wind => x.WindSpeedKmh,
                        _ => x.TemperatureC
                    },
                    isSevere = x.IsSevere,
                    temperature = x.TemperatureC,
                    humidity = x.Humidity,
                    windSpeed = x.WindSpeedKmh
                });

            await _outZen.InvokeVoidAsync("setWeatherChart", recent, _selectedMetric.ToString());
        }

        private enum WeatherMetric
        {
            Temperature,
            Humidity,
            Wind
        }

        private WeatherMetric _selectedMetric = WeatherMetric.Temperature;

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        private void ClickInfo(int id) => SelectedId = id;

        private RainAlertDTO _currentRainAlert;

        private void OnHeavyRainReceived(RainAlertDTO alert)
        {
            _ = InvokeAsync(() =>
            {
                _currentRainAlert = alert;
                StateHasChanged();
            });
        }
        private void ShowRainAlert(RainAlertDTO alert)
        {
            _currentRainAlert = alert;
            InvokeAsync(StateHasChanged);
        }
        private Task OnAlertDismissed()
        {
            _currentRainAlert = null;
            return Task.CompletedTask;
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_outZen is not null)
                    await _outZen.InvokeVoidAsync("disposeOutZen", new { mapId = _mapId });
            }
            catch { }

            try
            {
                if (_outZen is not null)
                    await _outZen.DisposeAsync();
                WeatherHub.HeavyRainReceived -= OnHeavyRainReceived;
            }
            catch { }
            
            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }

        private async Task GenerateOne()
        {
            var dto = await WeatherForecastService.GenerateNewForecastAsync();
            if (dto is null) return;

            allWeatherForecasts.Insert(0, dto);
            visibleWeatherForecasts.Insert(0, dto);
            WeatherForecastLists.Insert(0, dto);

            if (_outZen is not null)
            {
                await AddWeatherMarkerAsync(dto);
                await _outZen.InvokeVoidAsync("fitToMarkers");
                await UpdateChartAsync();
            }

            StateHasChanged();
        }

        private static string GetWeatherCss(bool isSevere)
            => isSevere ? "severe--true" : "severe--false";
    }
}








































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





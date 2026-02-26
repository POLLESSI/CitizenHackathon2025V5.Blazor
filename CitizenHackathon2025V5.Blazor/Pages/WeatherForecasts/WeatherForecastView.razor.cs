using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Linq;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastView : OutZenMapPageBase
    {
#nullable disable
        [Inject] public WeatherForecastService WeatherForecastService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        //private const string ApiBase = "https://localhost:7254";

        
        
        // ===== Map contract =====
        protected override string ScopeKey => "weatherforecastview";
        protected override string MapId => "leafletMap-weatherforecastview";
        // boot options
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // Optional
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        private readonly ConcurrentQueue<ClientWeatherForecastDTO> _pendingHubUpdates = new();

        private static string WfMarkerId(int id) => $"wf:{id}";

        // ===== Data =====
        public List<ClientWeatherForecastDTO> WeatherForecastLists { get; set; } = new();
        private List<ClientWeatherForecastDTO> allWeatherForecasts = new();
        private List<ClientWeatherForecastDTO> visibleWeatherForecasts = new();

        private int currentIndex = 0;
        private const int PageSize = 20;
        private long _lastFitTicks;

        // ===== UI =====
        private ElementReference ScrollContainerRef;
        private string _q = string.Empty;
        private bool _onlyRecent;
        public int SelectedId { get; set; }

        // ===== State =====
        private bool _disposed;
        private bool _dataLoaded;

        // ===== Hub =====
        public HubConnection hubConnection { get; set; }

        // ===== Alerts =====
        private RainAlertDTO _currentRainAlert;

        // ===== Chart =====
        private WeatherMetric _selectedMetric = WeatherMetric.Temperature;

        public enum WeatherMetric { Temperature, Humidity, Wind }

        // ----------------------------
        // Init
        // ----------------------------
        protected override async Task OnInitializedAsync()
        {
            // REST initial
            var fetched = await WeatherForecastService.GetAllAsync() ?? new List<ClientWeatherForecastDTO>();

            WeatherForecastLists = fetched;
            allWeatherForecasts = fetched;

            visibleWeatherForecasts.Clear();
            currentIndex = 0;
            LoadMoreItems();

            _dataLoaded = true;

            // SignalR
            var url = HubUrls.Build(WeatherForecastHubMethods.HubPath);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<ClientWeatherForecastDTO>(WeatherForecastHubMethods.ToClient.ReceiveForecast, async dto =>
            {
                if (_disposed || dto is null) return;

                UpsertById(WeatherForecastLists, dto);
                UpsertById(allWeatherForecasts, dto);
                UpsertVisible(dto);

                if (!IsMapBooted)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await ApplySingleWeatherMarkerAsync(dto);
                await UpdateChartAsync();
                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>(WeatherForecastHubMethods.ToClient.EventArchived, async id =>
            {
                if (_disposed) return;

                WeatherForecastLists.RemoveAll(c => c.Id == id);
                allWeatherForecasts.RemoveAll(c => c.Id == id);
                visibleWeatherForecasts.RemoveAll(c => c.Id == id);

                if (IsMapBooted)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.removeCrowdMarker", WfMarkerId(id), ScopeKey); } catch { }
                }

                await InvokeAsync(StateHasChanged);
            });

            // Rain alert event (if you have it)
            WeatherHub.HeavyRainReceived += OnHeavyRainReceived;

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine($"✅ [WF] Connected: {url}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ [WF] hub start failed: {ex.Message}");
            }

            await InvokeAsync(StateHasChanged);
            await NotifyDataLoadedAsync(fit: true);
        }

        private async Task LoadAllAsync()
        {
            var fetched = await WeatherForecastService.GetAllAsync() ?? new List<ClientWeatherForecastDTO>();

            WeatherForecastLists = fetched;
            allWeatherForecasts = fetched;

            visibleWeatherForecasts.Clear();
            currentIndex = 0;
            LoadMoreItems();
        }

        private async Task StartSignalRAsync()
        {
            var url = HubUrls.Build(WeatherForecastHubMethods.HubPath);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<ClientWeatherForecastDTO>(WeatherForecastHubMethods.ToClient.ReceiveForecast, async dto =>
            {
                if (_disposed || dto is null) return;

                UpsertById(WeatherForecastLists, dto);
                UpsertById(allWeatherForecasts, dto);
                UpsertVisible(dto);

                if (!IsMapBooted)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await ApplySingleWeatherMarkerAsync(dto);
                await UpdateChartAsync();
                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>(WeatherForecastHubMethods.ToClient.EventArchived, async id =>
            {
                if (_disposed) return;

                WeatherForecastLists.RemoveAll(c => c.Id == id);
                allWeatherForecasts.RemoveAll(c => c.Id == id);
                visibleWeatherForecasts.RemoveAll(c => c.Id == id);

                if (IsMapBooted)
                {
                    try { await MapInterop.RemoveCrowdMarkerAsync(WfMarkerId(id), ScopeKey); } catch { }
                }

                await InvokeAsync(StateHasChanged);
            });

            // Rain alert (if you actually use it)
            WeatherHub.HeavyRainReceived += OnHeavyRainReceived;

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine($"✅ [WF] Connected: {url}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ [WF] hub start failed: {ex.Message}");
            }
        }

        protected override async Task SeedAsync(bool fit)
        {
            if (_disposed) return;

            // ✅ clear the same family as you upsert (crowd/details)
            await MapInterop.ClearCrowdMarkersAsync(ScopeKey);

            foreach (var dto in allWeatherForecasts)
                await ApplySingleWeatherMarkerAsync(dto);

            if (fit && allWeatherForecasts.Count > 0)
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await MapInterop.FitToDetailsAsync(ScopeKey);
            }

            Console.WriteLine($"[WF] SeedAsync: booted={IsMapBooted} count={allWeatherForecasts.Count}");
        }

        protected override async Task OnMapReadyAsync()
        {
            try { await MapInterop.RefreshSizeAsync(ScopeKey); } catch { }
            await Task.Delay(50);
            try { await MapInterop.RefreshSizeAsync(ScopeKey); } catch { }

            while (_pendingHubUpdates.TryDequeue(out var dto))
                await ApplySingleWeatherMarkerAsync(dto);

            await UpdateChartAsync();
        }

        // ----------------------------
        // Map markers
        // ----------------------------


        private async Task ReseedWeatherMarkersAsync(bool fit)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            try { await MapInterop.ClearCrowdMarkersAsync(ScopeKey); ; } catch { }

            foreach (var dto in allWeatherForecasts)
                await ApplySingleWeatherMarkerAsync(dto);

            await FitThrottledAsync();
            if (fit)
            {
                await FitThrottledAsync();
            }
        }
        private async Task FitThrottledAsync(int ms = 250)
        {
            var now = Environment.TickCount64;
            if (now - _lastFitTicks < ms) return;
            _lastFitTicks = now;

            try
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await MapInterop.FitToDetailsAsync(ScopeKey);
            }
            catch { }
        }


        private async Task ApplySingleWeatherMarkerAsync(ClientWeatherForecastDTO dto)
        {
            if (_disposed) return;
            if (!IsMapBooted) { _pendingHubUpdates.Enqueue(dto); return; }
            if (dto is null) return;

            var lat = dto.Latitude;
            var lng = dto.Longitude;

            if (!double.IsFinite(lat) || !double.IsFinite(lng) || (lat == 0 && lng == 0) ||
                lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                // fallback safe
                lat = 50.85;
                lng = 4.35;
            }

            var level = dto.IsSevere ? 4 : 2;

            await MapInterop.UpsertCrowdMarkerAsync(
                id: WfMarkerId(dto.Id),
                lat: lat,
                lng: lng,
                level: level,
                info: new
                {
                    kind = "weather",
                    title = dto.Summary ?? "Weather",
                    description = $"Temp: {dto.TemperatureC}°C, Vent: {dto.WindSpeedKmh} km/h (Maj {dto.DateWeather:HH:mm:ss})",
                    weatherType = dto.WeatherType.ToString(),
                    isWeather = true,
                    icon = "🌦️"
                },
                scopeKey: ScopeKey
            );
        }

        // ----------------------------
        // List helpers
        // ----------------------------
        private static void UpsertById(List<ClientWeatherForecastDTO> list, ClientWeatherForecastDTO dto)
        {
            if (list is null || dto is null) return;
            var i = list.FindIndex(x => x.Id == dto.Id);
            if (i >= 0) list[i] = dto; else list.Add(dto);
        }

        private void UpsertVisible(ClientWeatherForecastDTO dto)
        {
            var idx = visibleWeatherForecasts.FindIndex(x => x.Id == dto.Id);
            if (idx >= 0) visibleWeatherForecasts[idx] = dto;
            else visibleWeatherForecasts.Insert(0, dto);
        }

        private void LoadMoreItems()
        {
            var next = allWeatherForecasts.Skip(currentIndex).Take(PageSize).ToList();
            visibleWeatherForecasts.AddRange(next);
            currentIndex += next.Count;
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<double>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<double>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<double>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allWeatherForecasts.Count)
            {
                LoadMoreItems();
                await InvokeAsync(StateHasChanged);
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

        private void ToggleRecent()
        {
            _onlyRecent = !_onlyRecent;
            if (IsMapBooted) _ = ReseedWeatherMarkersAsync(fit: false);
        }

        private void ClickInfo(int id) => SelectedId = id;

        // ----------------------------
        // Chart
        // ----------------------------
        private async Task ChangeMetric(WeatherMetric metric)
        {
            _selectedMetric = metric;
            await UpdateChartAsync();
        }

        private async Task UpdateChartAsync()
        {
            if (_disposed) return;
            if (!IsMapBooted) return;
            if (allWeatherForecasts.Count == 0) return;

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
                })
                .ToList();

            // IMPORTANT: this must exist in OutZenInterop (wrapper).
            // If you don't have it, see the 2 options below.
            try
            {
                var canvasId = $"weatherChart-{ScopeKey}";
                await JS.InvokeVoidAsync("OutZenInterop.setWeatherChart", recent, _selectedMetric.ToString(), ScopeKey, canvasId);
            }
            catch
            {
                // fallback: If your wrapper doesn't have this hook, leave it silent.
            }
        }

        // ----------------------------
        // Alerts
        // ----------------------------
        private void OnHeavyRainReceived(RainAlertDTO alert)
        {
            _ = InvokeAsync(() =>
            {
                _currentRainAlert = alert;
                StateHasChanged();
            });
        }

        private Task OnAlertDismissed()
        {
            _currentRainAlert = null;
            return Task.CompletedTask;
        }

        // ----------------------------
        // Generate
        // ----------------------------
        private async Task GenerateOne()
        {
            var dto = await WeatherForecastService.GenerateNewForecastAsync();
            if (dto is null) return;

            allWeatherForecasts.Insert(0, dto);
            visibleWeatherForecasts.Insert(0, dto);
            WeatherForecastLists.Insert(0, dto);

            if (IsMapBooted)
            {
                await ApplySingleWeatherMarkerAsync(dto);
                await UpdateChartAsync();
            }

            StateHasChanged();
        }

        private static string GetWeatherCss(bool isSevere)
            => isSevere ? "severe--true" : "severe--false";

        // ----------------------------
        // Dispose
        // ----------------------------
        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }

            WeatherHub.HeavyRainReceived -= OnHeavyRainReceived;
        }

    }
}









































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





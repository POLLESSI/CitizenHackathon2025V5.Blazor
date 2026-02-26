using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.DTOs.Options;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventView : OutZenMapPageBase
    {
    #nullable disable
        [Inject] public EventService EventService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        // ✅ Inject MapInterop since we do NOT inherit OutZenMapPageBase

        [Parameter, SupplyParameterFromQuery(Name = "detailId")]
        public int? DetailId { get; set; }

        // ===== Map "contract" local =====
        protected override string ScopeKey => "eventview";
        protected override string MapId => "leafletMap-eventview";
        private string _token;
        protected override int DefaultZoom => 14;
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        // Optional
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override string AllowedMarkerPrefix => "event:";
        protected override bool ClearAllOnMapReady => true; // Optional but recommended for a single-type page
        private static string EvMarkerId(int id) => $"event:{id}";

        private ElementReference ScrollContainerRef;

        private bool _initialMarkersApplied;
        private bool _disposed;

        private int? BestMatchId { get; set; }
        private long _lastFitTicks;
        private CancellationTokenSource _searchCts;

        public List<ClientEventDTO> Events { get; set; } = new();
        private List<ClientEventDTO> allEvents = new();
        private List<ClientEventDTO> visibleEvents = new();

        private int currentIndex = 0;
        private const int PageSize = 20;

        private string _q = string.Empty;
        private bool _onlyRecent;

        public HubConnection hubConnection { get; set; }

        private readonly ConcurrentQueue<ClientEventDTO> _pendingHubUpdates = new();

        protected override async Task OnInitializedAsync()
        {
            // REST
            var fetched = (await EventService.GetLatestEventAsync())
                .Where(e => e != null)
                .Select(e => e!)
                .ToList();

            Events = fetched;
            allEvents = fetched;

            visibleEvents.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // SignalR
            var url = HubUrls.Build(EventHubMethods.HubPath);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHubHandlers();

            try { await hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[EventView] Hub start failed: {ex.Message}"); }

            await NotifyDataLoadedAsync(fit: true);
        }
        protected override async Task OnMapReadyAsync()
        {
            // Optional: fit after boot (if you want)
            try
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
            }
            catch { }

            // Don't push the markers here: SeedAsync will do it when _dataLoaded=true
        }
        protected override async Task SeedAsync(bool fit)
        {
            // 1) Clear markers of scope
            await MapInterop.ClearCrowdMarkersAsync(ScopeKey);

            // 2) Source (filtered if you want)
            var source = allEvents; // or FilterEvent(...).ToList()

            foreach (var ev in source)
            {
                var lvl = MapCrowdLevelFromExpected(ev.ExpectedCrowd);

                var (lat, lng) = Normalize(ev.Latitude, ev.Longitude);

                await MapInterop.UpsertCrowdMarkerAsync(
                    id: EvMarkerId(ev.Id),
                    lat: lat,
                    lng: lng,
                    level: lvl,
                    info: new
                    {
                        title = ev.Name ?? "Event",
                        description = $"{ev.DateEvent:yyyy-MM-dd HH:mm}",
                        kind = "event",
                        icon = "🎪",
                        expected = ev.ExpectedCrowd
                    },
                    scopeKey: ScopeKey
                );
            }

            if (fit && source.Count > 0)
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await MapInterop.FitToDetailsAsync(ScopeKey);
            }
        }

        private static (double lat, double lng) Normalize(double lat, double lng)
        {
            if (!double.IsFinite(lat) || !double.IsFinite(lng) ||
                lat < -90 || lat > 90 || lng < -180 || lng > 180 ||
                (lat == 0 && lng == 0))
                return (50.85, 4.35);

            return (lat, lng);
        }
        private async Task ApplySingleEventMarkerAsync(ClientEventDTO dto)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;
            if (dto is null) return;

            var (lat, lng) = Normalize(dto.Latitude, dto.Longitude);
            var lvl = MapCrowdLevelFromExpected(dto.ExpectedCrowd);

            await MapInterop.UpsertCrowdMarkerAsync(
                id: EvMarkerId(dto.Id),
                lat: lat,
                lng: lng,
                level: lvl,
                info: new
                {
                    title = dto.Name ?? "Event",
                    description = $"{dto.DateEvent:yyyy-MM-dd HH:mm}",
                    kind = "event",
                    icon = "🎪"
                },
                scopeKey: ScopeKey
            );
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
        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;

            hubConnection.On<ClientEventDTO>("ReceiveEventUpdate", async dto =>
            {
                UpsertLocal(dto);

                if (!_initialMarkersApplied || !IsMapBooted)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await UpsertMarkerAsync(dto, fit: false);
                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("EventArchived", async id =>
            {
                Events.RemoveAll(c => c.Id == id);
                allEvents.RemoveAll(c => c.Id == id);
                visibleEvents.RemoveAll(c => c.Id == id);

                if (IsMapBooted)
                {
                    try {
                        await MapInterop.RemoveCrowdMarkerAsync(EvMarkerId(id), ScopeKey);
                        await SyncMapMarkersAsync(fit: false);
                    }
                    catch { }
                }

                await InvokeAsync(StateHasChanged);
            });
        }

        private async Task UpsertMarkerAsync(ClientEventDTO dto, bool fit)
        {
            var lvl = MapCrowdLevelFromExpected(dto.ExpectedCrowd);

            try
            {
                double lat = dto.Latitude;
                double lng = dto.Longitude;

                if (!double.IsFinite(lat) || !double.IsFinite(lng) ||
                    lat < -90 || lat > 90 || lng < -180 || lng > 180 ||
                    (lat == 0 && lng == 0))
                {
                    lat = 50.85;
                    lng = 4.35;
                }

                await MapInterop.UpsertCrowdMarkerAsync(
                    id: EvMarkerId(dto.Id),
                    lat: lat,
                    lng: lng,
                    level: lvl,
                    info: new { title = dto.Name, description = $"{dto.DateEvent:yyyy-MM-dd HH:mm}", kind = "event", icon = "🎪" },
                    scopeKey: ScopeKey
                );

                if (fit)
                {
                    await FitThrottledAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventView] UpsertMarkerAsync failed: {ex.Message}");
            }
        }

        private async Task SyncMapMarkersAsync(bool fit)
        {
            var baseSource = visibleEvents.Any() ? visibleEvents : allEvents;
            var source = FilterEvent(baseSource).ToList();

            try
            {
                await MapInterop.ClearCrowdMarkersAsync(ScopeKey);

                foreach (var ev in source)
                {
                    var lvl = MapCrowdLevelFromExpected(ev.ExpectedCrowd);

                    double lat = ev.Latitude;
                    double lng = ev.Longitude;

                    if (!double.IsFinite(lat) || !double.IsFinite(lng) ||
                        lat < -90 || lat > 90 || lng < -180 || lng > 180 ||
                        (lat == 0 && lng == 0))
                    {
                        lat = 50.85;
                        lng = 4.35;
                    }

                    await MapInterop.UpsertCrowdMarkerAsync(
                        id: EvMarkerId(ev.Id),
                        lat: lat,
                        lng: lng,
                        level: lvl,
                        info: new { title = ev.Name, description = $"{ev.DateEvent:yyyy-MM-dd HH:mm}", kind = "event", icon = "🎪" },
                        scopeKey: ScopeKey
                    );
                }

                if (source.Any())
                {
                    await MapInterop.RefreshSizeAsync(ScopeKey);
                    if (fit) await MapInterop.FitToDetailsAsync(ScopeKey);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventView] SyncMapMarkersAsync failed: {ex.Message}");
            }
        }
        private IEnumerable<ClientEventDTO> FilterEvent(IEnumerable<ClientEventDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                            || x.Latitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase)
                            || x.Longitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.DateEvent >= cutoff);
        }

        private void LoadMoreItems()
        {
            var next = allEvents.Skip(currentIndex).Take(PageSize).ToList();
            visibleEvents.AddRange(next);
            currentIndex += next.Count;
        }

        private void UpsertLocal(ClientEventDTO dto)
        {
            void Upsert(List<ClientEventDTO> list)
            {
                var i = list.FindIndex(c => c.Id == dto.Id);
                if (i >= 0) list[i] = dto; else list.Add(dto);
            }

            Upsert(Events);
            Upsert(allEvents);

            var j = visibleEvents.FindIndex(c => c.Id == dto.Id);
            if (j >= 0) visibleEvents[j] = dto;
        }

        private static int MapCrowdLevelFromExpected(int? expected)
        {
            var v = Math.Max(1, expected ?? 1);
            if (v < 3000) return 1;
            if (v < 5000) return 2;
            if (v < 10000) return 3;
            return 4;
        }

        private async Task OnSearchInput(ChangeEventArgs e)
        {
            _q = e.Value?.ToString() ?? "";

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                await Task.Delay(200, ct);
                await HighlightBestEventAsync(ct);
            }
            catch (TaskCanceledException) { }
        }

        private void ToggleRecent()
        {
            _onlyRecent = !_onlyRecent;
            _ = SyncMapMarkersAsync(fit: true);
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allEvents.Count)
            {
                LoadMoreItems();
                await SyncMapMarkersAsync(fit: false);
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task HighlightBestEventAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (!_initialMarkersApplied) return;

            var q = _q?.Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                BestMatchId = null;
                await InvokeAsync(StateHasChanged);
                try {
                    await MapInterop.ClearCrowdMarkersAsync(ScopeKey);
                    await SyncMapMarkersAsync(fit: false);
                }
                catch { }
                return;
            }

            var best = allEvents
                .Select(ev => new { ev.Id, Score = ScoreMatch(q, ev.Name) })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best is null)
            {
                BestMatchId = null;
                await InvokeAsync(StateHasChanged);
                try {
                    await MapInterop.ClearCrowdMarkersAsync(ScopeKey);
                    await SyncMapMarkersAsync(fit: false);
                }
                catch { }
                return;
            }

            BestMatchId = best.Id;
            await InvokeAsync(StateHasChanged);

            try
            {
                await FitThrottledAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventView] highlightEventMarker failed: {ex.Message}");
            }
        }

        private static int ScoreMatch(string q, string name)
        {
            q = (q ?? "").ToLowerInvariant();
            var s = (name ?? "").ToLowerInvariant();
            if (s.Length == 0) return 0;
            if (s == q) return 1000;
            if (s.StartsWith(q)) return 500;
            if (s.Contains(q)) return 200;
            return 0;
        }

        private async Task FocusEventAsync(int id)
        {
            BestMatchId = id;
            await InvokeAsync(StateHasChanged);

            try { await JS.InvokeVoidAsync("OutZen.scrollRowIntoView", $"event-row-{id}"); } catch { }

            try
            {
                await FitThrottledAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[EventView] FocusEventAsync failed: {ex.Message}");
            }
        }

        private async Task CloseDetail()
        {
            Navigation.NavigateTo("/eventview", replace: false);
            DetailId = null;
            BestMatchId = null;

            try
            {
                if (IsMapBooted)
                {
                    await FitThrottledAsync();
                }
            }
            catch { }

            await InvokeAsync(StateHasChanged);
        }

        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





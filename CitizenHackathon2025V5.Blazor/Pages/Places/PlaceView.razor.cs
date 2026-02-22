using CitizenHackathon2025.Blazor.DTOs;
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
using System.Reflection;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceView : OutZenMapPageBase
    {
#nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public PlaceService PlaceService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        private const string ApiBase = "https://localhost:7254";

        //private IJSObjectReference _outzen;
        private bool _booted;
        private bool _mapBooted;
        private bool _initialDataApplied;
        private bool _interopReady;
        private bool _mapInitStarted;
        private bool _disposed;

        public HubConnection hubConnection { get; set; }

        // Data
        public List<ClientPlaceDTO> Places { get; set; } = new();
        private List<ClientPlaceDTO> allPlaces = new();
        private List<ClientPlaceDTO> visiblePlaces = new();
        private int currentIndex = 0;
        private long _lastFitTicks;
        private const int PageSize = 20;
        protected override string ScopeKey => "placeview";
        protected override string MapId => "leafletMap-placeview";
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // Optionnel
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        private static string PlMarkerId(int id) => $"place:{id}";
        private string _token;

        private bool _dataLoaded;

        // UI state
        private ElementReference ScrollContainerRef;
        private string _qBacking;

        private string _q
        {
            get => _qBacking;
            set
            {
                if (_qBacking == value) return;
                _qBacking = value;

                // fire-and-forget contrôlé (debounced)
                _ = DebouncedHighlightAsync();
            }
        }
        private bool _onlyRecent; // placeholder if you filter by date
        private CancellationTokenSource _searchCts;

        private int? BestMatchId { get; set; }

        private string _speedId = $"speedRange-{Guid.NewGuid():N}";
        private int SelectedId;

        private readonly ConcurrentQueue<ClientPlaceDTO> _pendingHubUpdates = new();

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            try
            {
                var fetched = (await PlaceService.GetLatestPlaceAsync()).ToList();
                Places = fetched;
                allPlaces = fetched;
                visiblePlaces.Clear();
                currentIndex = 0;
                LoadMoreItems();

                _dataLoaded = true;
                await InvokeAsync(StateHasChanged);

                Console.WriteLine($"[PlaceView] REST fetched places = {allPlaces.Count}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlaceView] Init REST failed: {ex.Message}");
                Places = new();
                allPlaces = new();
                visiblePlaces = new();
            }

            // 2) SignalR

            var url = HubUrls.Build(HubPaths.Place);// => https://localhost:7254/hubs/placeHub

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            RegisterHubHandlers();

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine("[PlaceView] Hub started.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlaceView] Hub start failed: {ex.Message}");
            }
        }

        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;
            hubConnection.On<ClientPlaceDTO>(PlaceHubMethods.ToClient.NewPlace, async dto =>
            {
                // Map DTO API -> Client DTO
                var client = new ClientPlaceDTO
                {
                    Id = dto.Id,
                    Name = dto.Name,
                    Type = dto.Type,
                    Indoor = dto.Indoor,
                    Latitude = (double)dto.Latitude,
                    Longitude = (double)dto.Longitude,
                    Capacity = dto.Capacity,
                    Tag = dto.Tag ?? ""
                };

                UpsertLocal(client);

                if (!_booted)
                {
                    _pendingHubUpdates.Enqueue(client);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await AddOrUpdatePlaceMarkerAsync(client, fit: true);
                await InvokeAsync(StateHasChanged);
            });
        }

        private void UpsertLocal(ClientPlaceDTO dto)
        {
            void Upsert(List<ClientPlaceDTO> list)
            {
                var i = list.FindIndex(p => p.Id == dto.Id);
                if (i >= 0) list[i] = dto; else list.Add(dto);
            }

            Upsert(Places);
            Upsert(allPlaces);

            var j = visiblePlaces.FindIndex(p => p.Id == dto.Id);
            if (j >= 0) visiblePlaces[j] = dto;
        }
        
        private async Task AddOrUpdatePlaceMarkerAsync(ClientPlaceDTO dto, bool fit = false)
        {
            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude))
                return;

            var level =
                dto.Capacity >= 3500 ? 1 :
                dto.Capacity >= 1500 ? 2 :
                dto.Capacity >= 150 ? 3 : 4;

            var desc = $"{dto.Type ?? "Unknown"}"
                       + (dto.Indoor ? " (indoor)" : " (outdoor)")
                       + $" • Cap: {dto.Capacity}"
                       + (string.IsNullOrWhiteSpace(dto.Tag) ? "" : $" • Tag: {dto.Tag}");

            await EnsureOutZenAsync();

            await JS.InvokeAsync<bool>(
                "OutZenInterop.addOrUpdateCrowdMarker",
                PlMarkerId(dto.Id),
                dto.Latitude,
                dto.Longitude,
                dto.Type,
                new { title = dto.Name, desc, icon = "🏰" },
                ScopeKey
            );

            if (fit)
            {
                await FitThrottledAsync();
            }

            Console.WriteLine($"[PlaceView] Send place marker #{dto.Id}: {dto.Latitude},{dto.Longitude}");
        }
        private async Task SyncMapMarkersAsync(bool fit = true)
        {
            Console.WriteLine($"[PlaceView] SyncMapMarkersAsync: visiblePlaces={visiblePlaces.Count}, allPlaces={allPlaces.Count}");

            try
            {
                await EnsureOutZenAsync();
                await JS.InvokeAsync<bool>("OutZenInterop.clearCrowdMarkers", ScopeKey); // ⚠️ Wrapper needs to be exposed too
                foreach (var pl in FilterPlace(visiblePlaces))
                    await AddOrUpdatePlaceMarkerAsync(pl, fit: false);

                if (visiblePlaces.Any() && fit)
                {
                    await FitThrottledAsync();
                }
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ [PlaceView] JSInterop failed in SyncMapMarkersAsync: {jsex.Message}");
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
        private Task EnsureOutZenAsync()
            => JS.InvokeVoidAsync("OutZen.ensure").AsTask();

        private async Task DebouncedHighlightAsync()
        {
            if (!_dataLoaded) return;

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                await Task.Delay(200, ct);
                await HighlightBestMatchAsync(ct);
                await InvokeAsync(StateHasChanged);
            }
            catch (TaskCanceledException) { }
        }
        private void LoadMoreItems()
        {
            var next = allPlaces.Skip(currentIndex).Take(PageSize).ToList();
            visiblePlaces.AddRange(next);
            currentIndex += next.Count;
        }
        private async Task HandleScroll()
        {
            try
            {
                var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
                var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
                var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

                if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allPlaces.Count)
                {
                    LoadMoreItems();
                    await SyncMapMarkersAsync(fit: false);
                    StateHasChanged();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlaceView] HandleScroll error: {ex.Message}");
            }
        }

        private IEnumerable<ClientPlaceDTO> FilterPlace(IEnumerable<ClientPlaceDTO> source)
        {
            var q = _q?.Trim();

            return source.Where(x =>
                string.IsNullOrEmpty(q)
                || (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (x.Type ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        }
        protected override async Task OnMapReadyAsync()
        {
            // À ce stade : map bootée + container OK
            await FitThrottledAsync();
            await Task.Delay(50);
            await FitThrottledAsync();

            await ReseedPlaceMarkersAsync(fit: true);
        }
        private async Task ReseedPlaceMarkersAsync(bool fit)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            foreach (var dto in allPlaces)
                await ApplySinglePlaceMarkerAsync(dto);

            await FitThrottledAsync();
            if (fit)
            {
                await FitThrottledAsync();
            }
        }
        private async Task ApplySinglePlaceMarkerAsync(ClientPlaceDTO dto)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;
            if (dto is null) return;

            var lat = dto.Latitude;
            var lng = dto.Longitude;

            if (!double.IsFinite(lat) || !double.IsFinite(lng) || (lat == 0 && lng == 0) ||
                lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                lat = 50.85;
                lng = 4.35;
            }

            await JS.InvokeVoidAsync(
                "OutZenInterop.addOrUpdateCrowdMarker",
                PlMarkerId(dto.Id),
                lat,
                lng,
                new
                {
                    name = dto.Name ?? "Place",
                    type = dto.Type,
                    indoor = true,
                    capacity = dto.Capacity,
                    icon = "🏰",
                    tag = dto.Tag
                },
                ScopeKey
            );
        }

        private async Task HighlightBestMatchAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;

            var q = _q?.Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                BestMatchId = null;
                await InvokeAsync(StateHasChanged);

                await JS.InvokeVoidAsync("clearPlaceHighlight");

                return;
            }

            // best match on all seats (or visiblePlaces if you prefer)
            var best = allPlaces
                .Select(p => new
                {
                    p.Id,
                    p.Name,
                    Score = ScoreMatch(q, p.Name, p.Type, p.Tag)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .FirstOrDefault();

            if (best is null)
            {
                BestMatchId = null;
                await InvokeAsync(StateHasChanged);

                
                await JS.InvokeVoidAsync("clearPlaceHighlight");

                return;
            }

            // ✅ table highlighting
            BestMatchId = best.Id;
            await InvokeAsync(StateHasChanged);

            // ✅ highlight marker (if map ready)
            await JS.InvokeAsync<bool>("OutZenInterop.highlightPlaceMarker", best.Id, new
            {
                openPopup = true,
                panTo = true,
                dimOthers = false
            }, ScopeKey);
        }

        private static int ScoreMatch(string q, params string[] fields)
        {
            q = q.ToLowerInvariant();
            var score = 0;

            foreach (var f in fields)
            {
                var s = (f ?? "").ToLowerInvariant();
                if (string.IsNullOrWhiteSpace(s)) continue;

                if (s == q) score += 1000;
                else if (s.StartsWith(q)) score += 500;
                else if (s.Contains(q)) score += 200;
            }
            return score;
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        private void Select(int id)
        {
            if (id <= 0)
            {
                Console.WriteLine("[PlaceView] Ignoring click: id <= 0 payload without ID ?)");
                return;
            }
            SelectedId = id;
            Console.WriteLine($"[PlaceView] SelectedId = {SelectedId}");
        }

        private void CloseDetail() => SelectedId = 0;

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
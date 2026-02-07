using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Reflection;
//PlaceView: scopeKey = $"place:{PlaceId}", mapId = $"outzenMap_place_{PlaceId}"

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceView : ComponentBase, IAsyncDisposable
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

        public HubConnection hubConnection { get; set; }

        // Data
        public List<ClientPlaceDTO> Places { get; set; } = new();
        private List<ClientPlaceDTO> allPlaces = new();
        private List<ClientPlaceDTO> visiblePlaces = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        private const string ScopeKey = "places"; // Stable scope for this page
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
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (_mapBooted || !_dataLoaded) return;
            _mapBooted = true;

            await EnsureOutZenAsync();

            // (0) security measures are in place (if you return to the page).
            await JS.InvokeVoidAsync("OutZenInterop.disposeOutZen", new { mapId = "placeMap", scopeKey = ScopeKey });

            // (1) wait container exists
            for (var i = 0; i < 20; i++)
            {
                var ok = await JS.InvokeAsync<bool>("checkElementExists", "placeMap");
                if (ok) break;
                await Task.Delay(50);
                if (i == 19) return;
            }

            // (2) boot
            var booted = await JS.InvokeAsync<bool>("OutZenInterop.bootOutZen", new
            {
                mapId = "placeMap",
                scopeKey = ScopeKey,
                center = new[] { 50.89, 4.34 },
                zoom = 13,
                force = true
            });

            if (!booted) { _mapBooted = false; return; }

            // (3) add markers
            foreach (var place in Places)
                await AddOrUpdatePlaceMarkerAsync(place, fit: false);

            while (_pendingHubUpdates.TryDequeue(out var dto))
                await AddOrUpdatePlaceMarkerAsync(dto, fit: false);

            // (4) resize + fit
            await JS.InvokeAsync<bool>("OutZenInterop.refreshMapSize", ScopeKey); // if you expose refreshMapSize in OutZenInterop
            await JS.InvokeAsync<bool>("OutZenInterop.fitToMarkers", ScopeKey);   // idem

            _initialDataApplied = true;
            _booted = true;
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
                $"pl:{dto.Id}",
                dto.Latitude,
                dto.Longitude,
                level,
                new { title = dto.Name, description = desc, icon = "🏰" },
                ScopeKey
            );

            if (fit)
            {
                await JS.InvokeAsync<bool>("OutZenInterop.fitToMarkers", ScopeKey);
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
                    await JS.InvokeAsync<bool>("OutZenInterop.refreshMapSize", ScopeKey);
                    await JS.InvokeAsync<bool>("OutZenInterop.fitToMarkers", ScopeKey);
                }
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ [PlaceView] JSInterop failed in SyncMapMarkersAsync: {jsex.Message}");
            }
        }
        private async Task EnsureOutZenAsync()
        {
            if (_interopReady) return;
            await JS.InvokeVoidAsync("OutZen.ensure"); // load the module via outzen-interop.js
            _interopReady = true;
        }
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
        private async Task OnSearchChanged(ChangeEventArgs e)
        {
            _q = e.Value?.ToString();

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                await Task.Delay(200, ct); // debounce
                await HighlightBestMatchAsync(ct);
                await InvokeAsync(StateHasChanged); // if you want to update the “_q” badge
            }
            catch (TaskCanceledException) { }
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

        public async ValueTask DisposeAsync()
        {
            //if (_outzen is null)
            //    return;

            try
            {
                await EnsureOutZenAsync();
                await JS.InvokeVoidAsync("OutZenInterop.disposeOutZen", new { mapId = "placeMap", scopeKey = ScopeKey });
            }
            catch { }

            try { if (hubConnection is not null) await hubConnection.DisposeAsync(); } catch { }

            // Bonus: Stop Hub properly
            try { if (hubConnection is not null) await hubConnection.DisposeAsync(); } catch { }
        }
        private Task AfterSearchBind()
        {
            throw new NotImplementedException();
        }
    }
}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
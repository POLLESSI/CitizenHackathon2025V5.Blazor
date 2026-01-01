using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Pages.OutZens;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventView : IAsyncDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public EventService EventService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Parameter, SupplyParameterFromQuery(Name = "detailId")]
        public int? DetailId { get; set; }

        private IJSObjectReference _outzen;

        public List<ClientEventDTO> Events { get; set; } = new();
        private List<ClientEventDTO> allEvents = new();
        private List<ClientEventDTO> visibleEvents = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        private int _applyOnce = 0;

        private int? BestMatchId { get; set; }
        private CancellationTokenSource _searchCts;
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q = string.Empty;
        private bool _onlyRecent;
        private bool _booted;
        private bool _initialDataApplied;

        private readonly ConcurrentQueue<ClientEventDTO> _pendingHubUpdates = new();
        protected override async Task OnInitializedAsync()
        {
            // 1) Initial REST
            var fetched = (await EventService.GetLatestEventAsync()).Where(e => e != null).Select(e => e!).ToList();

            Console.WriteLine($"[EventView] fetched events: {fetched.Count}");

            if (fetched.Count > 0)
            {
                var first = fetched[0];
                Console.WriteLine($"[EventView] first event: Id={first.Id}, Name={first.Name}, Lat={first.Latitude}, Lng={first.Longitude}");
            }

            Events = fetched;
            allEvents = fetched;
            visibleEvents.Clear();
            currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

            // EventHubMethods.HubPath = "events"
            var hubPath = EventHubMethods.HubPath.TrimStart('/'); // "events"

            var url = $"{hubBaseUrl}/hubs/{hubPath}";
            // => https://localhost:7254/hubs/events

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
        }

        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;

            hubConnection.On<ClientEventDTO>("ReceiveEventUpdate", async dto =>
            {
                UpsertLocal(dto);

                // If the map isn't ready yet → we buffer it
                if (!_booted || _outzen is null)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                var lvl = MapCrowdLevelFromExpected(dto.ExpectedCrowd);

                await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, lvl,
                    new { title = dto.Name, description = $"Event {dto.DateEvent:yyyy-MM-dd HH:mm}" });

                try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("EventArchived", async id =>
            {
                Events.RemoveAll(c => c.Id == id);
                allEvents.RemoveAll(c => c.Id == id);
                visibleEvents.RemoveAll(c => c.Id == id);

                if (_booted && _outzen is not null)
                {
                    await _outzen.InvokeVoidAsync("removeCrowdMarker", id.ToString());
                    await SyncMapMarkersAsync(fit: false);
                }

                await InvokeAsync(StateHasChanged);
            });
        }


        private static int MapCrowdLevelFromExpected(int? expected)
        {
            var v = Math.Max(1, expected ?? 1);
            // Adjust these thresholds to your domain
            if (v < 3000) return 1;         // Low
            if (v < 5000) return 2;        // Medium
            if (v < 10000) return 3;        // High
            return 4;                      // Critical
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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // 1) Boot map only on the first render
            if (firstRender && !_booted)
            {
                for (var i = 0; i < 40; i++)
                {
                    if (await JS.InvokeAsync<bool>("checkElementExists", "leafletMap")) break;
                    await Task.Delay(150);
                    if (i == 39) return;
                }

                _outzen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

                await _outzen.InvokeVoidAsync("bootOutZen", new
                {
                    mapId = "leafletMap",
                    center = new[] { 50.89, 4.34 },
                    zoom = 13,
                    enableChart = false,
                    force = true
                });

                // ready polling
                var ready = false;
                for (var i = 0; i < 40; i++)
                {
                    try { ready = await _outzen.InvokeAsync<bool>("isOutZenReady"); } catch { }
                    if (ready) break;
                    await Task.Delay(150);
                }
                if (!ready) return;

                try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }

                _booted = true;
                return;
            }

            // 2) Apply the markers ONLY ONCE after boot + data
            if (_booted && allEvents.Any() && Interlocked.Exchange(ref _applyOnce, 1) == 0)
            {
                await SyncMapMarkersAsync(fit: true);

                // flush hub buffer (If you want)
                while (_pendingHubUpdates.TryDequeue(out var dto))
                {
                    var lvl = MapCrowdLevelFromExpected(dto.ExpectedCrowd);
                    await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                        dto.Id.ToString(), dto.Latitude, dto.Longitude, lvl,
                        new { title = dto.Name, description = $"{dto.DateEvent:yyyy-MM-dd HH:mm}" });
                }

                try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }
            }
        }

        private async Task SafeRefreshMap()
        {
            if (_outzen is null) return;
            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }
            try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }
        }

        // Called when the URL changes: /eventview <-> /eventdetail/{id}
        protected override async Task OnParametersSetAsync()
        {
            if (DetailId is > 0) SelectedId = DetailId.Value;
            else SelectedId = 0;

            if (_booted) await SafeRefreshMap();
        }

        private async Task CloseDetail()
        {
            Navigation.NavigateTo("/eventview", replace: false); // SPA historical guard
            await SafeRefreshMap();
            StateHasChanged();

        }

        private async Task SyncMapMarkersAsync(bool fit = true)
        {
            if (_outzen is null)
            {
                Console.WriteLine("[EventView] SyncMapMarkersAsync: _outzen is null");
                return;
            }

            // 🔎 Data source for the map
            var baseSource = visibleEvents.Any() ? visibleEvents : allEvents;
            var source = FilterEvent(baseSource).ToList();

            Console.WriteLine($"[EventView] SyncMapMarkersAsync: visible={visibleEvents.Count}, all={allEvents.Count}, source(filtered)={source.Count}");

            try
            {
                // 1) Clean up existing markers
                await _outzen.InvokeVoidAsync("clearCrowdMarkers");

                // 2) Adding markers
                foreach (var ev in source)
                {
                    var lvl = MapCrowdLevelFromExpected(ev.ExpectedCrowd);

                    await _outzen.InvokeVoidAsync(
                        "addOrUpdateCrowdMarker",
                        ev.Id.ToString(),
                        ev.Latitude,
                        ev.Longitude,
                        lvl,
                        new
                        {
                            title = ev.Name,
                            description = $"{ev.DateEvent:yyyy-MM-dd HH:mm}"
                        });
                }

                // 3) Adjusting the view if there is at least one marker
                if (source.Any())
                {
                    await _outzen.InvokeVoidAsync("refreshMapSize");

                    if (fit)
                    {
                        await _outzen.InvokeVoidAsync("fitToMarkers");
                        await Task.Delay(150);
                        await _outzen.InvokeVoidAsync("fitToMarkers");
                    }
                }
                else
                {
                    Console.WriteLine("[EventView] SyncMapMarkersAsync: aucune donnée pour la carte.");
                }
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ JSInterop failed in SyncMapMarkersAsync: {jsex.Message}");
            }
        }
        private void LoadMoreItems()
        {
            var next = allEvents.Skip(currentIndex).Take(PageSize).ToList();
            visibleEvents.AddRange(next);
            currentIndex += next.Count;
        }

        private async Task OnSearchInput(ChangeEventArgs e)
        {
            _q = e.Value?.ToString() ?? "";

            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                await Task.Delay(200, ct); // debounce
                await HighlightBestEventAsync(ct);
            }
            catch (TaskCanceledException) { }
        }

        private async Task OnSearchChanged(ChangeEventArgs _)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                await Task.Delay(200, ct); // debounce
                await HighlightBestEventAsync(ct);
            }
            catch (TaskCanceledException) { }
        }

        private async Task HighlightBestEventAsync(CancellationToken ct)
        {
            if (ct.IsCancellationRequested) return;
            if (_outzen is null) return;           // map not ready
            if (!_booted) return;                  // guard

            var q = _q?.Trim();
            if (string.IsNullOrWhiteSpace(q))
            {
                BestMatchId = null;
                await InvokeAsync(StateHasChanged);
                try { await _outzen.InvokeVoidAsync("clearPlaceHighlight"); } catch { }
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
                try { await _outzen.InvokeVoidAsync("clearPlaceHighlight"); } catch { }
                return;
            }

            BestMatchId = best.Id;
            await InvokeAsync(StateHasChanged);

            // Focus marker (no reset map)
            try
            {
                await _outzen.InvokeAsync<bool>("highlightEventMarker", best.Id, new
                {
                    openPopup = true,
                    panTo = true,
                    zoom = 15,
                    dimOthers = false
                });
            }
            catch (JSException ex)
            {
                Console.Error.WriteLine($"[EventView] highlightEventMarker failed: {ex.Message}");
            }
        }

        private static int ScoreMatch(string q, string name)
        {
            q = q.ToLowerInvariant();
            var s = (name ?? "").ToLowerInvariant();
            if (s.Length == 0) return 0;
            if (s == q) return 1000;
            if (s.StartsWith(q)) return 500;
            if (s.Contains(q)) return 200;
            return 0;
        }


        private async Task FocusEventAsync(int id)
        {
            if (_outzen is null) return;

            BestMatchId = id;               // surligne aussi la ligne
            await InvokeAsync(StateHasChanged);

            // scroll (optionnel)
            try { await JS.InvokeVoidAsync("OutZen.scrollRowIntoView", $"event-row-{id}"); } catch { }

            // focus marker
            await _outzen.InvokeAsync<bool>("highlightEventMarker", id, new
            {
                openPopup = true,
                panTo = true,
                zoom = 16,
                dimOthers = false
            });
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

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allEvents.Count)
            {
                LoadMoreItems();
                await SyncMapMarkersAsync(fit: false);
                StateHasChanged();
            }
        }

        private void ToggleRecent()
        {
            _onlyRecent = !_onlyRecent;
            _ = SyncMapMarkersAsync(fit: true);
        }

        private void GoToDetail(int id) => SelectedId = id;

        public async ValueTask DisposeAsync()
        {
            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
            if (_outzen is not null)
            {
                try { await _outzen.DisposeAsync(); } catch { }
            }
        }
    }
}





















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





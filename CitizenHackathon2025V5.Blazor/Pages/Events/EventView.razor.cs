using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Pages.OutZens;
using CitizenHackathon2025V5.Blazor.Client.Services;
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
            // 1) REST initial
            var fetched = (await EventService.GetLatestEventAsync()).Where(e => e != null).Select(e => e!).ToList();
            Events = fetched;
            allEvents = fetched;
            visibleEvents.Clear();
            currentIndex = 0;
            LoadMoreItems();
            await InvokeAsync(StateHasChanged);

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = Config["SignalR:HubBase"]?.TrimEnd('/')
                             ?? $"{apiBaseUrl}/hubs"; // fallback if no specific configuration

            var url = $"{hubBaseUrl}{HubPaths.Event}"; // => https://.../hubs/events

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
            if (!firstRender || _booted)
                return;

            // 0) Wait until the #leafletMap container actually exists in the DOM
            for (var i = 0; i < 40; i++) // ~6s max
            {
                var ok = await JS.InvokeAsync<bool>("checkElementExists", "leafletMap");
                if (ok)
                    break;

                await Task.Delay(150);

                if (i == 39)
                {
                    Console.WriteLine("❌ [EventView] Map container #leafletMap not found after retries.");
                    return;
                }
            }

            // 1) Import ESM OutZen
            _outzen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            var height = await JS.InvokeAsync<int>("eval",
                "document.getElementById('leafletMap')?.clientHeight || 0");
            await JS.InvokeVoidAsync("console.log", "Map element height:", height);

            // 2) Single boot (force: true to ensure the map is recreated cleanly)
            try
            {
                await _outzen.InvokeVoidAsync("bootOutZen", new
                {
                    mapId = "leafletMap",
                    center = new[] { 50.89, 4.34 },
                    zoom = 13,
                    enableChart = false,
                    force = true
                });
            }
            catch (JSException jsEx)
            {
                Console.Error.WriteLine($"❌ [EventView] bootOutZen JS error: {jsEx.Message}");
                return;
            }

            // 3) Polling for readiness (without restarting bootOutZen in a loop)
            var ready = false;
            for (var i = 0; i < 40; i++)
            {
                try
                {
                    ready = await _outzen.InvokeAsync<bool>("isOutZenReady");
                    if (ready)
                        break;
                }
                catch
                {
                    // Ignore, we'll try again
                }

                await Task.Delay(150);
            }

            if (!ready)
            {
                Console.WriteLine("❌ [EventView] OutZen not ready after boot / polling.");
                return;
            }

            // 4) Refresh + initial marker synchronization
            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }

            await SyncMapMarkersAsync(fit: true);

            // Flush any queued SignalR updates (as in your current version).
            while (_pendingHubUpdates.TryDequeue(out var dto))
            {
                var lvl = MapCrowdLevelFromExpected(dto.ExpectedCrowd);
                await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, lvl,
                    new { title = dto.Name, description = $"Event {dto.DateEvent:yyyy-MM-dd HH:mm}" });
            }
            try
            {
                await _outzen.InvokeVoidAsync("fitToMarkers");
            }
            catch { }

            _initialDataApplied = true;
            _booted = true;
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
            if (_outzen is null) return;

            try
            {
                // 1) Clean up existing markers
                await _outzen.InvokeVoidAsync("clearCrowdMarkers");

                // 2) Replace the visible markers
                foreach (var ev in FilterEvent(visibleEvents))
                {
                    var lvl = MapCrowdLevelFromExpected(ev.ExpectedCrowd);
                    await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                        ev.Id.ToString(), ev.Latitude, ev.Longitude, lvl,
                        new { title = ev.Name, description = $"{ev.DateEvent:yyyy-MM-dd HH:mm}" });
                }

                // 3) Card alignment (optional)
                if (visibleEvents.Any())
                {
                    await _outzen.InvokeVoidAsync("refreshMapSize");

                    if (fit)
                    {
                        await _outzen.InvokeVoidAsync("fitToMarkers");
                        await Task.Delay(150);
                        await _outzen.InvokeVoidAsync("fitToMarkers");
                    }
                }
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ JSInterop failed in SyncMapMarkersAsync: {jsex.Message}");
            }
            try
            {
                await _outzen.InvokeVoidAsync("refreshMapSize");
                await _outzen.InvokeVoidAsync("fitToMarkers");
                await Task.Delay(150);
                await _outzen.InvokeVoidAsync("fitToMarkers");
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"[WARN] refreshMap/fitToMarkers failed: {jsex.Message}");
            }

        }


        private void LoadMoreItems()
        {
            var next = allEvents.Skip(currentIndex).Take(PageSize).ToList();
            visibleEvents.AddRange(next);
            currentIndex += next.Count;
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





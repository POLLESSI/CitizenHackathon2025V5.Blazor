using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using CitizenHackathon2025.Blazor.DTOs;

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

        private const string ApiBase = "https://localhost:7254";

        private IJSObjectReference _outzen;
        private bool _booted;
        private bool _initialDataApplied;

        public HubConnection hubConnection { get; set; }

        // Data
        public List<ClientPlaceDTO> Places { get; set; } = new();
        private List<ClientPlaceDTO> allPlaces = new();
        private List<ClientPlaceDTO> visiblePlaces = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        private bool _dataLoaded;

        // UI state
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent; // placeholder si tu filtres un jour par date

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
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";

            // SignalR:HubBase is considered to be the root (https://localhost:7254)
            var hubBaseConfig = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

            // We guarantee that we have /hubs as the suffix, without doubling it.
            string hubRoot;
            if (hubBaseConfig.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase))
                hubRoot = hubBaseConfig;                  // already OK
            else
                hubRoot = hubBaseConfig + "/hubs";        // we add /hubs

            var url = $"{hubRoot}{HubPaths.Place}";       // => https://localhost:7254/hubs/placeHub

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
            hubConnection.On<ClientPlaceDTO>("ReceivePlaceUpdate", async dto =>
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

                if (!_booted || _outzen is null)
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
            if (_booted || !_dataLoaded)
                return;

            // 0) Wait until the #placeMap container exists
            for (var i = 0; i < 10; i++)
            {
                var ok = await JS.InvokeAsync<bool>("checkElementExists", "placeMap");
                if (ok) break;
                await Task.Delay(150);
                if (i == 9)
                {
                    Console.WriteLine("❌ [PlaceView] Map container not found (placeMap).");
                    return;
                }
            }

            // 1) Importer ESM OutZen
            _outzen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            var booted = await _outzen.InvokeAsync<bool>("bootOutZen", new
            {
                mapId = "placeMap",
                center = new[] { 50.89, 4.34 },
                zoom = 13,
                enableChart = false,
                force = true
            });

            if (!booted)
            {
                Console.WriteLine("❌ [PlaceView] OutZen boot failed.");
                return;
            }

            // 2) Initial seed markers
            foreach (var place in Places)
            {
                await AddOrUpdatePlaceMarkerAsync(place, fit: false);
            }

            // 3) Refresh + initial sync
            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }
            await SyncMapMarkersAsync(fit: true);

            // 4) Flush pending updates
            while (_pendingHubUpdates.TryDequeue(out var dto))
            {
                await AddOrUpdatePlaceMarkerAsync(dto, fit: false);
            }

            try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }

            _initialDataApplied = true;
            _booted = true;

        }


        private async Task AddOrUpdatePlaceMarkerAsync(ClientPlaceDTO dto, bool fit = false)
        {
            if (_outzen is null)
                return;

            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude))
                return;

            // We derive a simple "level" from the capacity (pure visual convention)
            var level =
                dto.Capacity >= 3500 ? 1 :
                dto.Capacity >= 1500 ? 2 :
                dto.Capacity >= 150 ? 3 : 4;

            var desc = $"{dto.Type ?? "Unknown"}"
                       + (dto.Indoor ? " (indoor)" : " (outdoor)")
                       + $" • Cap: {dto.Capacity}"
                       + (string.IsNullOrWhiteSpace(dto.Tag) ? "" : $" • Tag: {dto.Tag}");

            await _outzen.InvokeVoidAsync(
                "addOrUpdateCrowdMarker",
                dto.Id.ToString(),
                dto.Latitude,
                dto.Longitude,
                level,
                new
                {
                    title = dto.Name ?? $"Place #{dto.Id}",
                    description = desc
                });

            if (fit)
            {
                try { await _outzen.InvokeVoidAsync("fitToMarkers"); }
                catch (JSException ex)
                {
                    Console.Error.WriteLine($"[PlaceView] fitToMarkers error: {ex.Message}");
                }
            }

            Console.WriteLine($"[PlaceView] Send place marker #{dto.Id}: {dto.Latitude},{dto.Longitude}");
        }


        private async Task SyncMapMarkersAsync(bool fit = true)
        {
            if (_outzen is null)
                return;
            Console.WriteLine($"[PlaceView] SyncMapMarkersAsync: visiblePlaces={visiblePlaces.Count}, allPlaces={allPlaces.Count}");

            try
            {
                await _outzen.InvokeVoidAsync("clearCrowdMarkers");

                foreach (var pl in FilterPlace(visiblePlaces))
                {
                    await AddOrUpdatePlaceMarkerAsync(pl, fit: false);
                }

                if (visiblePlaces.Any() && fit)
                {
                    await _outzen.InvokeVoidAsync("refreshMapSize");
                    await _outzen.InvokeVoidAsync("fitToMarkers");
                    await Task.Delay(150);
                    await _outzen.InvokeVoidAsync("fitToMarkers");
                }
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ [PlaceView] JSInterop failed in SyncMapMarkersAsync: {jsex.Message}");
            }
            var snapshot = FilterPlace(allPlaces).ToList();
            foreach (var pl in snapshot)
            {
                await AddOrUpdatePlaceMarkerAsync(pl, fit: false);
            }

            if (fit)
            {
                try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }
            }
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
            if (_outzen != null)
            {
                try { await _outzen.DisposeAsync(); } catch { }
            }
        }
    }
}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
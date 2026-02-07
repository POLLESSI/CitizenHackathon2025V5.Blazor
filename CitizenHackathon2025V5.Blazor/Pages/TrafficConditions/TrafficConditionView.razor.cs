using System.Collections.Concurrent;
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
//TrafficConditionView: scopeKey = "traffic", mapId = "outzenMap_traffic"

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionView : IAsyncDisposable
    {
#nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public TrafficConditionService TrafficConditionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        private const string ApiBase = "https://localhost:7254";

        // ID unique pour la carte TrafficCondition
        private const string MapId = "outzenMap_traffic"; /*"trafficMap"*/

        private IJSObjectReference _outZen;

        public List<ClientTrafficConditionDTO> TrafficConditions { get; set; } = new();
        private readonly List<ClientTrafficConditionDTO> allTrafficConditions = new();
        private readonly List<ClientTrafficConditionDTO> visibleTrafficConditions = new();

        private int currentIndex = 0;
        private const int PageSize = 20;

        private readonly ConcurrentQueue<ClientTrafficConditionDTO> _pendingHubUpdates = new();
        private readonly string _speedId = $"speedRange-{Guid.NewGuid():N}";

        public int SelectedId { get; set; }
        private HubConnection hubConnection;

        // Fields used by the .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private int _refreshing = 0;
        private bool _onlyRecent;

        // State map / markers
        private bool _booted;
        private bool _markersSeeded;

        protected override async Task OnInitializedAsync()
        {
            // 1) Initial REST: loads the latest conditions into the database
            var fetched = (await TrafficConditionService.GetLatestTrafficConditionAsync())?.ToList() ?? new();
            Console.WriteLine($"[TrafficConditionView] Initial TrafficConditions count = {fetched.Count}");

            TrafficConditions = fetched;
            allTrafficConditions.Clear();
            allTrafficConditions.AddRange(fetched);

            visibleTrafficConditions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // Final URL : https://localhost:7254/hubs/trafficHub
            var url = HubUrls.Build(TrafficConditionHubMethods.HubPath);

            Console.WriteLine($"[Traffic-Client] Hub URL = {url}");

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // === Event: a traffic condition is added/updated ===
            hubConnection.On(TrafficConditionHubMethods.ToClient.NotifyNewTraffic, async () =>
            {
                try
                {
                    // 1) Re-fetch from the API (source of truth)
                    var latest = (await TrafficConditionService.GetLatestTrafficConditionAsync())?.ToList() ?? new();

                    // 2) Simple upsert: here we replace everything (more reliable and faster to implement)
                    TrafficConditions = latest;

                    allTrafficConditions.Clear();
                    allTrafficConditions.AddRange(latest);

                    visibleTrafficConditions.Clear();
                    currentIndex = 0;
                    LoadMoreItems();

                    // 3) Map: If ready, resync markers
                    if (_booted && _outZen is not null)
                    {
                        try { await _outZen.InvokeVoidAsync("clearCrowdMarkers", "traffic"); } catch { }

                        foreach (var tc in TrafficConditions)
                            await AddOrUpdateTrafficMarkerAsync(tc, fit: false);

                        try { await _outZen.InvokeVoidAsync("refreshMapSize", "traffic"); } catch { }
                        try { await _outZen.InvokeVoidAsync("fitToMarkers", "traffic"); } catch { }
                    }

                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[TrafficConditionView] Refresh after notify failed: {ex.Message}");
                }
            });
            // === Event: a condition is archived / deleted ===
            hubConnection.On<int>(
                TrafficConditionHubMethods.ToClient.TrafficCleared,
                async id =>
                {
                    TrafficConditions.RemoveAll(c => c.Id == id);
                    allTrafficConditions.RemoveAll(c => c.Id == id);
                    visibleTrafficConditions.RemoveAll(c => c.Id == id);

                    if (_booted && _outZen is not null)
                    {
                        try { await _outZen.InvokeVoidAsync("removeCrowdMarker", $"tr:{id}", "traffic"); } catch { }
                        try { await _outZen.InvokeVoidAsync("fitToMarkers", "traffic"); } catch { }
                    }

                    await InvokeAsync(StateHasChanged);
                });

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine("[Traffic-Client] hubConnection.StartAsync() OK.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Traffic-Client] hubConnection.StartAsync() FAILED: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds or updates a marker on the map for a given traffic condition.
        /// </summary>
        private async Task AddOrUpdateTrafficMarkerAsync(ClientTrafficConditionDTO dto, bool fit = false)
        {
            Console.WriteLine($"[TrafficConditionView] AddOrUpdateTrafficMarkerAsync Id={dto.Id}, lat={dto.Latitude}, lon={dto.Longitude}");

            if (_outZen is null)
            {
                Console.WriteLine("[TrafficConditionView] _outZen is null, skipping marker.");
                return;
            }

            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude))
            {
                Console.WriteLine($"[TrafficConditionView] Ignoring DTO {dto.Id}: invalid coords {dto.Latitude}, {dto.Longitude}");
                return;
            }

            int baseLevel = 0;
            if (dto.Level.HasValue && dto.Level.Value > 0)
            {
                baseLevel = dto.Level.Value;
            }

            int level;

            if (baseLevel > 0)
            {
                level = baseLevel;
            }
            else
            {
                var raw = dto.CongestionLevel?.Trim().ToLowerInvariant();

                level = raw switch
                {
                    "1" or "low" or "faible" or "freeflow" => 1, // ✅ Green
                    "2" or "medium" or "moyen" or "moderate" => 2, // ✅ orange
                    "3" or "high" or "heavy" or "jammed" => 3, // ✅ bright red
                    "4" or "severe" or "sévère" => 4, // ✅ dark red
                    _ => 0  // ✅ unknown -> gray
                };
            }


            var desc = $"{dto.CongestionLevel ?? "N/A"}"
                       + (string.IsNullOrWhiteSpace(dto.IncidentType) ? "" : $" • {dto.IncidentType}")
                       + (string.IsNullOrWhiteSpace(dto.Message) ? "" : $" • {dto.Message}")
                       + $" • {dto.DateCondition:yyyy-MM-dd HH:mm}";
            
            await _outZen.InvokeVoidAsync(
                "addOrUpdateCrowdMarker",
                $"tr:{dto.Id}",
                dto.Latitude,
                dto.Longitude,
                level,
                new
                {
                    title = dto.IncidentType ?? "Traffic",
                    description = desc,
                    isTraffic = true,
                    icon = "⚠️"
                },
                "traffic"
            );
            if (fit)
            {
                try { 
                    await _outZen.InvokeVoidAsync("refreshMapSize", "traffic");
                    await Task.Delay(50);
                    await _outZen.InvokeVoidAsync("refreshMapSize", "traffic");
                } catch { }
                try { await _outZen.InvokeVoidAsync("fitToMarkers", "traffic"); } catch { }
            }
        }

        /// <summary>
        /// Gradually fills the visible list (infinite scroll).
        /// </summary>
        private void LoadMoreItems()
        {
            var next = allTrafficConditions.Skip(currentIndex).Take(PageSize).ToList();
            visibleTrafficConditions.AddRange(next);
            currentIndex += next.Count;
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // boot initial
            if (firstRender)
            {
                var exists = await JS.InvokeAsync<bool>("eval", $"!!document.getElementById('{MapId}')");
                if (!exists) return;

                _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

                var ok = await _outZen.InvokeAsync<bool>("bootOutZen", new
                {
                    mapId = MapId,
                    scopeKey = "traffic",
                    zoom = 8,
                    enableChart = false,
                    force = true,
                    resetMarkers = true,
                    enableHybrid = false
                });

                Console.WriteLine($"[TrafficConditionView] bootOutZen traffic ok={ok}");
                if (!ok) return;

                _booted = true;
            }

            // ✅ IMPORTANT: On EACH render, if booted, the size is invalidated (debounced on the JS side).
            if (_booted && _outZen is not null)
            {
                try { await _outZen.InvokeVoidAsync("refreshMapSize", "traffic"); } catch { }
            }

            // seed markers only once
            if (_booted && !_markersSeeded && TrafficConditions.Count > 0 && _outZen is not null)
            {
                foreach (var tc in TrafficConditions)
                    await AddOrUpdateTrafficMarkerAsync(tc, fit: false);

                try { await _outZen.InvokeVoidAsync("fitToMarkers", "traffic"); } catch { }
                _markersSeeded = true;
            }
        }

        private async Task ReseedTrafficMarkersAsync(bool fit = true)
        {
            if (_outZen is null) return;
            try { await _outZen.InvokeVoidAsync("clearCrowdMarkers", "traffic"); } catch { }

            foreach (var tc in TrafficConditions)
                await AddOrUpdateTrafficMarkerAsync(tc, fit: false);

            try { await _outZen.InvokeVoidAsync("refreshMapSize", "traffic"); } catch { }
            if (fit) { try { await _outZen.InvokeVoidAsync("fitToMarkers", "traffic"); } catch { } }
        }

        private void ClickInfo(int id)
        {
            if (id <= 0)
            {
                Console.WriteLine("[TrafficConditionView] Ignoring Info click: id <= 0 (payload without ID ?)");
                return;
            }

            SelectedId = id;
            Console.WriteLine($"[TrafficConditionView] SelectedId = {SelectedId}");
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (currentIndex < allTrafficConditions.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        private IEnumerable<ClientTrafficConditionDTO> FilterTraffic(IEnumerable<ClientTrafficConditionDTO> source)
            => FilterTrafficCondition(source);

        private IEnumerable<ClientTrafficConditionDTO> FilterTrafficCondition(IEnumerable<ClientTrafficConditionDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x =>
                    string.IsNullOrEmpty(q)
                    || (!string.IsNullOrEmpty(x.IncidentType) && x.IncidentType.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(x.Message) && x.Message.Contains(q, StringComparison.OrdinalIgnoreCase))
                    || (!string.IsNullOrEmpty(x.CongestionLevel) && x.CongestionLevel.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !_onlyRecent || x.DateCondition >= cutoff);
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_outZen is not null)
                    await _outZen.InvokeVoidAsync("disposeOutZen", new { mapId = MapId, scopeKey = "traffic" });
            }
            catch { }

            try
            {
                if (_outZen is not null)
                    await _outZen.DisposeAsync();
            }
            catch { }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}


































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





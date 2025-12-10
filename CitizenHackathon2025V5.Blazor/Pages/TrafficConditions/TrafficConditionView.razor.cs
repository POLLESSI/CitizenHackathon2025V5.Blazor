using System.Collections.Concurrent;
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

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

        private const string ApiBase = "https://localhost:7254";

        // ID unique pour la carte TrafficCondition
        private const string MapId = "trafficMap";

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

            // 2) SignalR – same logic as WeatherForecastView
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase;

            // If ApiBaseUrl ends with "/api", we remove it to return to the root directory (https://localhost:7254)
            if (apiBaseUrl.EndsWith("/api", StringComparison.OrdinalIgnoreCase))
                apiBaseUrl = apiBaseUrl[..^4];

            // Final URL : https://localhost:7254/hubs/trafficHub
            var url = $"{apiBaseUrl}/hubs/{TrafficConditionHubMethods.HubPath}";

            Console.WriteLine($"[Traffic-Client] Hub URL = {url}");

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // === Event: a traffic condition is added/updated ===
            hubConnection.On<ClientTrafficConditionDTO>(
                TrafficConditionHubMethods.ToClient.TrafficUpdated,
                async dto =>
                {
                    if (dto is null) return;

                    void Upsert(List<ClientTrafficConditionDTO> list)
                    {
                        var i = list.FindIndex(g => g.Id == dto.Id);
                        if (i >= 0)
                            list[i] = dto;
                        else
                            list.Add(dto);
                    }

                    Upsert(TrafficConditions);
                    Upsert(allTrafficConditions);

                    var j = visibleTrafficConditions.FindIndex(c => c.Id == dto.Id);
                    if (j >= 0)
                        visibleTrafficConditions[j] = dto;
                    else
                        visibleTrafficConditions.Insert(0, dto);

                    if (!_booted || _outZen is null)
                    {
                        _pendingHubUpdates.Enqueue(dto);
                        await InvokeAsync(StateHasChanged);
                        return;
                    }

                    await AddOrUpdateTrafficMarkerAsync(dto, fit: true);
                    await InvokeAsync(StateHasChanged);
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
                        try { await _outZen.InvokeVoidAsync("removeCrowdMarker", id.ToString()); } catch { }
                        try { await _outZen.InvokeVoidAsync("fitToMarkers"); } catch { }
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
                dto.Id.ToString(),
                dto.Latitude,
                dto.Longitude,
                level,
                new
                {
                    title = dto.IncidentType ?? "Traffic",
                    description = desc,
                    isTraffic = true
                    // icon = "🚦"
                });

            if (fit)
            {
                try { await _outZen.InvokeVoidAsync("fitToMarkers"); } catch { }
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
            // 1) Boot the map at the first render
            if (firstRender && !_booted)
            {
                // 0) Wait until the container with the correct ID is present in the DOM
                for (var i = 0; i < 10; i++)
                {
                    var ok = await JS.InvokeAsync<bool>("checkElementExists", MapId);
                    if (ok) break;

                    await Task.Delay(150);

                    if (i == 9)
                    {
                        Console.WriteLine($"❌ [TrafficConditionView] Map container not found ({MapId}).");
                        return;
                    }
                }

                // 1) Importing the LeafletOutZen ESM module
                _outZen = await JS.InvokeAsync<IJSObjectReference>(
                    "import",
                    "/js/app/leafletOutZen.module.js");

                var booted = await _outZen.InvokeAsync<bool>(
                    "bootOutZen",
                    new
                    {
                        mapId = MapId,
                        center = new[] { 50.89, 4.34 },
                        zoom = 13,
                        enableChart = false,
                        force = true
                    });

                if (!booted)
                {
                    Console.WriteLine("❌ [TrafficConditionView] OutZen boot failed.");
                    return;
                }

                _booted = true;
                Console.WriteLine("[TrafficConditionView] OutZen boot OK.");
            }

            // 2) Marker seeds: as soon as the map boots and data is available, only once.
            if (_booted && !_markersSeeded && TrafficConditions.Count > 0 && _outZen is not null)
            {
                Console.WriteLine($"[TrafficConditionView] Seeding {TrafficConditions.Count} traffic markers…");

                foreach (var traffic in TrafficConditions)
                {
                    await AddOrUpdateTrafficMarkerAsync(traffic, fit: false);
                }

                try { await _outZen.InvokeVoidAsync("refreshMapSize"); } catch { }
                await Task.Delay(100);
                try { await _outZen.InvokeVoidAsync("fitToMarkers"); } catch { }

                // 🔍 DEBUG JS
                try { await _outZen.InvokeVoidAsync("debugDumpMarkers"); } catch { }

                // 3) Replay the updates received via SignalR before booting
                while (_pendingHubUpdates.TryDequeue(out var dto))
                {
                    await AddOrUpdateTrafficMarkerAsync(dto, fit: false);
                }

                try { await _outZen.InvokeVoidAsync("fitToMarkers"); } catch { }

                _markersSeeded = true;
            }
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
                {
                    await _outZen.DisposeAsync();
                }
            }
            catch
            {
                // ignore
            }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}


































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





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
using System.Linq;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionView : OutZenMapPageBase
    {
#nullable disable
        [Inject] public TrafficConditionService TrafficConditionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        // ===== Data =====
        public List<ClientTrafficConditionDTO> TrafficConditions { get; set; } = new();
        private readonly List<ClientTrafficConditionDTO> allTrafficConditions = new();
        private readonly List<ClientTrafficConditionDTO> visibleTrafficConditions = new();

        private const int PageSize = 20;
        private int currentIndex = 0;
        private long _lastFitTicks;

        // ===== UI state =====
        private ElementReference ScrollContainerRef;
        private string _q = string.Empty;
        private bool _onlyRecent;
        public int SelectedId { get; set; }

        // ===== Map contract =====
        protected override string ScopeKey => "trafficconditionview";
        protected override string MapId => "leafletMap-trafficconditionview";

        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // Optional
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        private static string TrMarkerId(int id) => $"trafficcondition:{id}";
        private string _token;

        // ===== State =====
        private bool _disposed;
        private bool _booted;
        private bool _mapInitStarted;
        private bool _markersSeeded;

        // ===== Hub =====
        private HubConnection hubConnection;

        // Buffer if hub pushes before map boot
        private readonly ConcurrentQueue<ClientTrafficConditionDTO> _pendingHubUpdates = new();

        // ----------------------------
        // Init
        // ----------------------------
        protected override async Task OnInitializedAsync()
        {
            // 1) REST
            var fetched = (await TrafficConditionService.GetLatestTrafficConditionAsync())?.ToList() ?? new();
            TrafficConditions = fetched;

            allTrafficConditions.Clear();
            allTrafficConditions.AddRange(fetched);

            visibleTrafficConditions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var url = HubUrls.Build(TrafficConditionHubMethods.HubPath); // ex: /hubs/trafficHub

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
                Console.WriteLine($"✅ [TrafficConditionView] Connected: {url}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ [TrafficConditionView] hub start failed: {ex.Message}");
            }
        }

        // ----------------------------
        // Hub handlers
        // ----------------------------
        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;

            // Server says "refresh": simplest reliable approach = refetch
            hubConnection.On(TrafficConditionHubMethods.ToClient.NotifyNewTraffic, async () =>
            {
                if (_disposed) return;

                try
                {
                    var latest = (await TrafficConditionService.GetLatestTrafficConditionAsync())?.ToList() ?? new();

                    TrafficConditions = latest;
                    allTrafficConditions.Clear();
                    allTrafficConditions.AddRange(latest);

                    visibleTrafficConditions.Clear();
                    currentIndex = 0;
                    LoadMoreItems();

                    if (_booted)
                        await ReseedTrafficMarkersAsync(fit: false);

                    await InvokeAsync(StateHasChanged);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ [TrafficConditionView] Refresh failed: {ex.Message}");
                }
            });

            hubConnection.On<int>(TrafficConditionHubMethods.ToClient.TrafficCleared, async id =>
            {
                if (_disposed) return;

                TrafficConditions.RemoveAll(c => c.Id == id);
                allTrafficConditions.RemoveAll(c => c.Id == id);
                visibleTrafficConditions.RemoveAll(c => c.Id == id);

                if (_booted)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.removeCrowdMarker", $"tr:{id}", ScopeKey); } catch { }
                }

                await InvokeAsync(StateHasChanged);
            });
        }
        protected override async Task OnMapReadyAsync()
        {
            // À ce stade : map bootée + container OK
            await FitThrottledAsync();
            await Task.Delay(50);
            await FitThrottledAsync();

            await ReseedTrafficMarkersAsync(fit: true);
        }


        // ----------------------------
        // Map marker operations
        // ----------------------------
        private async Task ReseedTrafficMarkersAsync(bool fit)
        {
            if (_disposed) return;
            if (!_booted) return;

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            foreach (var tc in TrafficConditions)
                await ApplySingleTrafficMarkerAsync(tc);

            await FitThrottledAsync();
            if (fit)
            {
                await FitThrottledAsync();
            }
        }

        private async Task ApplySingleTrafficMarkerAsync(ClientTrafficConditionDTO dto)
        {
            if (_disposed) return;
            if (!_booted) { _pendingHubUpdates.Enqueue(dto); return; }

            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) return;
            if (dto.Latitude == 0 && dto.Longitude == 0) return;

            var level = ResolveLevel(dto);

            var desc = $"{dto.CongestionLevel ?? "N/A"}"
                       + (string.IsNullOrWhiteSpace(dto.IncidentType) ? "" : $" • {dto.IncidentType}")
                       + (string.IsNullOrWhiteSpace(dto.Message) ? "" : $" • {dto.Message}")
                       + $" • {dto.DateCondition:yyyy-MM-dd HH:mm}";

            await JS.InvokeVoidAsync(
                "OutZenInterop.addOrUpdateCrowdMarker",
                TrMarkerId(dto.Id),
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
                ScopeKey
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
        private static int ResolveLevel(ClientTrafficConditionDTO dto)
        {
            if (dto.Level.HasValue && dto.Level.Value > 0)
                return dto.Level.Value;

            var raw = dto.CongestionLevel?.Trim().ToLowerInvariant();

            return raw switch
            {
                "1" or "low" or "faible" or "freeflow" => 1,
                "2" or "medium" or "moyen" or "moderate" => 2,
                "3" or "high" or "heavy" or "jammed" => 3,
                "4" or "severe" or "sévère" => 4,
                _ => 0
            };
        }

        // ----------------------------
        // Infinite scroll + filters
        // ----------------------------
        private void LoadMoreItems()
        {
            var next = allTrafficConditions.Skip(currentIndex).Take(PageSize).ToList();
            visibleTrafficConditions.AddRange(next);
            currentIndex += next.Count;
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allTrafficConditions.Count)
            {
                LoadMoreItems();
                await InvokeAsync(StateHasChanged);
            }
        }

        private IEnumerable<ClientTrafficConditionDTO> FilterTraffic(IEnumerable<ClientTrafficConditionDTO> source)
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

        private void ToggleRecent()
        {
            _onlyRecent = !_onlyRecent;
            // Optional: resync markers on filter
            if (_booted) _ = ReseedTrafficMarkersAsync(fit: false);
        }

        private void ClickInfo(int id)
        {
            if (id <= 0) return;
            SelectedId = id;
        }

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
        }
    }
}



































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





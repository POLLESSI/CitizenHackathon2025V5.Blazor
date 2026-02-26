// CrowdInfoView.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;
using CitizenHackathon2025V5.Blazor.Client.Shared;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using CrowdHub = CitizenHackathon2025.Contracts.Hubs.CrowdHubMethods;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoView : OutZenMapPageBase
    {
#nullable disable
        // ===== Inject =====
        [Inject] public CrowdInfoService CrowdInfoService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;

        private HubConnection _hub;

        // ===== Data =====
        public List<ClientCrowdInfoDTO> CrowdInfos { get; set; } = new();
        private readonly List<ClientCrowdInfoDTO> _visible = new();
        private List<ClientCrowdInfoDTO> _all = new();

        private const int PageSize = 25;
        private int _currentIndex = 0;
        private long _lastFitTicks;

        // ===== UI & Scroll =====
        private ElementReference ScrollContainerRef;
        private ElementReference TableScrollRef;
        private string _q = string.Empty;
        private bool _onlyRecent;

        // ===== Map contract =====
        protected override string ScopeKey => "crowdinfoview";
        protected override string MapId => "leafletMap-crowdinfoview";
        // boot options
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // Optional
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override string AllowedMarkerPrefix => "crowd:";
        protected override bool ClearAllOnMapReady => true;
        private static string CIMarkerId(int id) => $"crowd:{id}";
        private string _token;

        // ===== State =====
        private bool _disposed;
        private bool _initialDataApplied;

        // ===== Hub buffering until map ready =====
        private readonly ConcurrentQueue<ClientCrowdInfoDTO> _pendingHubUpdates = new();
        private readonly Dictionary<int, int> _lastLevels = new();

        public int SelectedId { get; set; }

        // ----------------------------
        // Lifecycle
        // ----------------------------
        protected override async Task OnInitializedAsync()
        {
            try
            {
                var fetched = (await CrowdInfoService.GetAllCrowdInfoAsync())?.ToList() ?? new List<ClientCrowdInfoDTO>();

                CrowdInfos = fetched;
                _all = fetched;

                _visible.Clear();
                _currentIndex = 0;
                LoadMoreItems();

                _lastLevels.Clear();
                foreach (var co in fetched)
                    _lastLevels[co.Id] = co.CrowdLevel;

                // SignalR
                await StartSignalRAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ CrowdInfoView init error: {ex.Message}");
            }
        }
        protected override async Task OnMapReadyAsync()
        {
            // At this stage: map booted + container OK
            await FitThrottledAsync();
            await Task.Delay(50);
            await FitThrottledAsync();

            await ReseedCrowdInfoMarkersAsync(fit: true);
            //await UpdateChartAsync();
        }

        private async Task ReseedCrowdInfoMarkersAsync(bool fit)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            foreach (var dto in _all)
                await ApplySingleCrowdInfoMarkerAsync(dto);

            await FitThrottledAsync();
            if (fit)
            {
                await FitThrottledAsync();
            }
        }

        private async Task ApplySingleCrowdInfoMarkerAsync(ClientCrowdInfoDTO dto)
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
                CIMarkerId(dto.Id),
                lat,
                lng,
                new
                {
                    locationname = dto.LocationName ?? "Crowd Info",
                    timestamp = dto.Timestamp,
                    crowdlevel = dto.CrowdLevel,
                    icon = "👥"
                },
                ScopeKey
            );
        }

        // ----------------------------
        // SignalR
        // ----------------------------
        private async Task StartSignalRAsync()
        {
            var hubUrl = HubUrls.Build(HubPaths.CrowdInfo);

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ClientCrowdInfoDTO>(CrowdHub.ToClient.ReceiveCrowdUpdate, async dto =>
            {
                if (_disposed) return;

                UpsertLocal(dto);

                var prev = _lastLevels.TryGetValue(dto.Id, out var p) ? p : 0;
                _lastLevels[dto.Id] = dto.CrowdLevel;

                if (_initialDataApplied && prev < 4 && dto.CrowdLevel == 4)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.beepCritical", dto.Id); } catch { }
                }

                if (!IsMapBooted)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await ApplySingleMarkerUpdateAsync(dto);

                // ⚠️ avoids systematic fitting with each update (it shakes up the map)
                // Keep it optional:
                // try { await JS.InvokeVoidAsync("OutZenInterop.fitToMarkers", ScopeKey); } catch { }

                await InvokeAsync(StateHasChanged);
            });

            _hub.On<int>(CrowdHub.ToClient.CrowdInfoArchived, async id =>
            {
                if (_disposed) return;

                CrowdInfos.RemoveAll(c => c.Id == id);
                _all.RemoveAll(c => c.Id == id);
                _visible.RemoveAll(c => c.Id == id);

                if (IsMapBooted)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.removeCrowdMarker", $"cr:{id}", ScopeKey); } catch { }
                    await SyncMapMarkersAsync(fit: false);
                }

                await InvokeAsync(StateHasChanged);
            });

            await _hub.StartAsync();
            Console.WriteLine($"✅ Connected to {hubUrl}");

            // Catch-up: if map already booted
            if (IsMapBooted && _visible.Count > 0)
                await SyncMapMarkersAsync(fit: true);
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
        private void UpsertLocal(ClientCrowdInfoDTO dto)
        {
            static void Upsert(List<ClientCrowdInfoDTO> list, ClientCrowdInfoDTO item)
            {
                var i = list.FindIndex(c => c.Id == item.Id);
                if (i >= 0) list[i] = item; else list.Add(item);
            }

            Upsert(CrowdInfos, dto);
            Upsert(_all, dto);

            var j = _visible.FindIndex(c => c.Id == dto.Id);
            if (j >= 0) _visible[j] = dto;
        }

        // ----------------------------
        // Map markers
        // ----------------------------
        private async Task SyncMapMarkersAsync(bool fit)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            var items = FilterCrowd(_visible).ToList();

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            foreach (var co in items)
                await ApplySingleMarkerUpdateAsync(co, alreadyBooted: true);

            if (fit && items.Any())
            {
                await FitThrottledAsync();
            }
        }

        private async Task ApplySingleMarkerUpdateAsync(ClientCrowdInfoDTO dto, bool alreadyBooted = false)
        {
            if (_disposed) return;
            if (!alreadyBooted && !IsMapBooted) return;

            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) return;
            if (dto.Latitude == 0 && dto.Longitude == 0) return;

            var lvl = Math.Clamp(dto.CrowdLevel, 1, SharedConstants.MaxCrowdLevel);

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateCrowdMarker",
                CIMarkerId(dto.Id),
                dto.Latitude,
                dto.Longitude,
                lvl,
                new
                {
                    title = dto.LocationName,
                    description = $"Maj {dto.Timestamp:HH:mm:ss}",
                    icon = "👥"
                },
                ScopeKey);
        }

        // ----------------------------
        // Pagination + filtering
        // ----------------------------
        private void LoadMoreItems()
        {
            var next = _all.Skip(_currentIndex).Take(PageSize).ToList();
            _visible.AddRange(next);
            _currentIndex += next.Count;
        }

        private IEnumerable<ClientCrowdInfoDTO> FilterCrowd(IEnumerable<ClientCrowdInfoDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x =>
                    string.IsNullOrEmpty(q) ||
                    (x.LocationName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Latitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Longitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.Timestamp >= cutoff);
        }

        private async Task HandleScroll()
        {
            if (_disposed) return;

            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", TableScrollRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && _currentIndex < _all.Count)
            {
                LoadMoreItems();
                if (IsMapBooted) await SyncMapMarkersAsync(fit: false);
                await InvokeAsync(StateHasChanged);
            }
        }

        private void ToggleRecent()
        {
            _onlyRecent = !_onlyRecent;
            if (IsMapBooted) _ = SyncMapMarkersAsync(fit: true);
        }

        private string Q
        {
            get => _q;
            set
            {
                _q = value;
                if (IsMapBooted) _ = SyncMapMarkersAsync(fit: true);
            }
        }

        // ----------------------------
        // Misc UI helpers
        // ----------------------------
        private static string InfoDesc(ClientCrowdInfoDTO co)
            => CrowdInfoSeverityHelpers.GetDescription(CrowdInfoSeverityHelpers.GetSeverity(co));

        private static string GetLevelCss(int level)
        {
            var safe = Math.Clamp(level, 0, 5);
            return $"info--lvl{safe}";
        }

        private void ClickInfo(int id) => SelectedId = id;

        private async Task TestMarkers()
        {
            if (!IsMapBooted) return;

            var testData = new[]
            {
            new { Id = 1, Lat = 50.89, Lng = 4.34, Level = 2, Name = "Medium" },
            new { Id = 2, Lat = 50.88, Lng = 4.35, Level = 3, Name = "High" },
            new { Id = 3, Lat = 50.90, Lng = 4.33, Level = 4, Name = "Critical" }
        };

            // Optional: clear
            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            foreach (var t in testData)
            {
                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateCrowdMarker",
                    $"test:{t.Id}",
                    t.Lat,
                    t.Lng,
                    t.Level,
                    new { title = t.Name, description = "Test", icon = "🧪" },
                    ScopeKey);
            }

            await FitThrottledAsync();
        }

        private async Task TestBoot()
        {
            // Your boot process is already complete at firstRender. Here you're just testing an insertion.
            if (!IsMapBooted) return;

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateCrowdMarker",
                "test:999",
                50.89, 4.34, 5,
                new { title = "TEST", description = "debug", icon = "🧪" },
                ScopeKey);

            await FitThrottledAsync();
        }

        // ----------------------------
        // Dispose
        // ----------------------------
        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;

            if (_hub is not null)
            {
                try { await _hub.StopAsync(); } catch { }
                try { await _hub.DisposeAsync(); } catch { }
            }
        }
    }
}














































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





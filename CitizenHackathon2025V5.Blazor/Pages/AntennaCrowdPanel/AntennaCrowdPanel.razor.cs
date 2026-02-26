using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Services.Interop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Xml.Linq;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.AntennaCrowdPanel
{
    public partial class AntennaCrowdPanel : OutZenMapPageBase
    {
    #nullable disable
        // --- Inject ---
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }
        [Inject] public AntennaService AntennaService { get; set; }
        [Inject] public AntennaCrowdService AntennaCrowdService { get; set; }

        // --- Parameters ---
        [Parameter] public int? SelectedEventId { get; set; }
        [Parameter] public int WindowMinutes { get; set; } = 10;
        [Parameter] public double MaxRadiusMeters { get; set; } = 5000;
        [Parameter] public int RefreshSeconds { get; set; } = 10;
        [Parameter] public bool RefreshViaHub { get; set; } = true;

        // --- Map contract ---
        protected override string ScopeKey => "antennacrowdpanel";
        protected override string MapId => "leafletMap-antennacrowdpanel";
        protected override bool EnableHybrid => false;
        protected override bool EnableCluster => false;

        // Activate the map if your .razor file contains a <div id="leafletMap-antennacrowdpanel">

        // boot options
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // Optional
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => false;

        // --- Data ---
        public List<ClientCrowdInfoAntennaDTO> Antennas { get; set; } = new();

        private List<ClientCrowdInfoAntennaDTO> _allAntennas = new();
        private readonly List<ClientCrowdInfoAntennaDTO> _visibleAntennas = new();
        private int _currentIndex = 0;
        private const int PageSize = 25;
        private long _lastFitTicks;
        private static string ANPMarkerId(int id) => $"ant:{id}";

        private ClientEventAntennaCrowdDTO _eventCrowd;
        private bool _loading;
        private bool _disposed;
        private bool _signalRStarted;

        // --- SignalR ---
        private HubConnection _hub;
        private readonly ConcurrentQueue<(int AntennaId, ClientAntennaCountsDTO Counts)> _pendingCountsUntilMap = new();
        private readonly Dictionary<int, ClientAntennaCountsDTO> _countsByAntenna = new();
        private readonly HashSet<int> _joinedAntennaGroups = new();
        private int? _joinedEventGroup;
        private long _lastRefreshTicks;

        // --- Timer ---
        private PeriodicTimer _timer;
        private CancellationTokenSource _cts;

        // --- UI refs ---
        private ElementReference ScrollContainerRef;
        private ElementReference TableScrollRef;

        private string _q = string.Empty;

        protected override async Task OnInitializedAsync()
        {
            _cts = new CancellationTokenSource();
            await LoadAntennasAsync();

            foreach (var a in _allAntennas.Take(10))
            {
                _countsByAntenna[a.Id] = new ClientAntennaCountsDTO { ActiveConnections = a.Id % 30, UniqueDevices = a.Id % 20 };
            }
            await StartSignalRAsync();
        }

        protected override async Task OnParametersSetAsync()
        {
            await RefreshEventCrowdAsync(force: true);
            await ResetTimerAsync();
            await EnsureEventGroupJoinedAsync();
        }

        // ✅ Called when the map is booted (by OutZenMapPageBase)
        private async Task FitThrottledAsync(int ms = 250)
        {
            var now = Environment.TickCount64;
            if (now - _lastFitTicks < ms) return;
            _lastFitTicks = now;

            try
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await JS.InvokeVoidAsync("OutZenInterop.fitToMarkers", ScopeKey, new { maxZoom = 17 });
            }
            catch { }
        }
        protected override async Task OnMapReadyAsync()
        {
            Console.WriteLine($"[AntennaCrowdPanel] OnMapReadyAsync booted={IsMapBooted}");
            // At this stage: map booted + container OK
            await FitThrottledAsync();
            if (_allAntennas.Count == 0)
            {
                // mini wait for first load (évite seed vide)
                for (int i = 0; i < 10 && _allAntennas.Count == 0; i++)
                    await Task.Delay(50);
            }

            if (_allAntennas.Count > 0)
                await NotifyDataLoadedAsync(fit: true);

            await Task.Delay(50);
            await FitThrottledAsync();

            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                try { await UpdateAntennaMarkerLevelAsync(item.AntennaId, item.Counts); } catch { }
            }
            //await UpdateChartAsync();
        }
        protected override async Task SeedAsync(bool fit)
        {
            Console.WriteLine($"[AntennaCrowdPanel] SeedAsync ENTER booted={IsMapBooted} antennas={_allAntennas.Count}");

            if (_disposed || !IsMapBooted) return;

            try { await JS.InvokeVoidAsync("OutZenInterop.clearAllMarkers", ScopeKey); } catch { }

            foreach (var dto in _allAntennas)
            {
                if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) continue;
                if (dto.Latitude == 0 && dto.Longitude == 0) continue;

                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker", new
                {
                    Id = dto.Id,
                    Latitude = dto.Latitude,
                    Longitude = dto.Longitude,
                    Name = dto.Name,
                    Description = dto.Description
                }, ScopeKey);
            }

            if (fit)
                await JS.InvokeVoidAsync("OutZenInterop.fitToAllMarkers", ScopeKey, new { maxZoom = 17 });

            var st = await JS.InvokeAsync<object>("OutZenInterop.dumpState", ScopeKey);
            Console.WriteLine($"[AntennaCrowdPanel] dumpState after seed = {System.Text.Json.JsonSerializer.Serialize(st)}");
        }
        private async Task ApplySingleAntennaCrowdPanelMarkerAsync(ClientCrowdInfoAntennaDTO dto)
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

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker", new
            {
                Id = dto.Id,
                Latitude = lat,
                Longitude = lng,
                Name = dto.Name,
                Description = dto.Description
            }, ScopeKey);
        }

        // ----------------------------
        // Antennas loading + pagination
        // ----------------------------

        private async Task LoadAntennasAsync()
        {
            try
            {
                Antennas = await AntennaService.GetAllAsync(_cts?.Token ?? CancellationToken.None);

                _allAntennas = Antennas
                    .Where(a => a is not null)
                    .OrderByDescending(a => a.Id)
                    .ToList();

                _visibleAntennas.Clear();
                _currentIndex = 0;
                LoadMoreItems();

                await JoinGroupsForVisibleAsync();
                await InvokeAsync(StateHasChanged);

                await NotifyDataLoadedAsync(fit: true);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ LoadAntennasAsync error: {ex.Message}");
            }
        }

        private IEnumerable<ClientCrowdInfoAntennaDTO> ApplyFilter(IEnumerable<ClientCrowdInfoAntennaDTO> src)
        {
            var q = _q?.Trim();
            if (string.IsNullOrWhiteSpace(q)) return src;

            return src.Where(a =>
                (a.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                (a.Description ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                a.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        private void LoadMoreItems()
        {
            var next = ApplyFilter(_allAntennas)
                .Skip(_currentIndex)
                .Take(PageSize)
                .ToList();

            _visibleAntennas.AddRange(next);
            _currentIndex += next.Count;
        }

        private async Task LoadMoreAndSyncAsync()
        {
            var before = _visibleAntennas.Select(a => a.Id).ToHashSet();
            LoadMoreItems();
            var added = _visibleAntennas.Where(a => !before.Contains(a.Id)).ToList();

            await JoinGroupsAsync(added.Select(a => a.Id));
            await SyncVisibleAntennaMarkersAsync(fit: false);

            await InvokeAsync(StateHasChanged);
        }

        private Task JoinGroupsForVisibleAsync()
            => JoinGroupsAsync(_visibleAntennas.Select(a => a.Id));

        private async Task JoinGroupsAsync(IEnumerable<int> antennaIds)
        {
            if (_hub is null) return;

            foreach (var id in antennaIds.Distinct())
            {
                if (_joinedAntennaGroups.Contains(id)) continue;
                try
                {
                    await _hub.InvokeAsync(CrowdInfoAntennaConnectionHubMethods.FromClient.JoinAntenna, id);
                    _joinedAntennaGroups.Add(id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ JoinAntenna({id}) failed: {ex.Message}");
                }
            }
        }

        private async Task EnsureEventGroupJoinedAsync()
        {
            if (_hub is null) return;
            var desired = SelectedEventId;

            if (_joinedEventGroup is not null && _joinedEventGroup != desired)
            {
                try { await _hub.InvokeAsync(CrowdInfoAntennaConnectionHubMethods.FromClient.LeaveEvent, _joinedEventGroup.Value); }
                catch { }
                _joinedEventGroup = null;
            }

            if (desired is not null && _joinedEventGroup != desired)
            {
                try
                {
                    await _hub.InvokeAsync(CrowdInfoAntennaConnectionHubMethods.FromClient.JoinEvent, desired.Value);
                    _joinedEventGroup = desired;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ JoinEvent({desired.Value}) failed: {ex.Message}");
                }
            }
        }
        private async Task StartSignalRAsync()
        {
            var hubUrl = HubUrls.Build(HubPaths.AntennaConnection);
            if (_signalRStarted) return;
            _signalRStarted = true;

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ClientAntennaCountsUpdateDTO>(
                CrowdInfoAntennaConnectionHubMethods.ToClient.AntennaCountsUpdated,
                async msg =>
                {
                    _countsByAntenna[msg.AntennaId] = msg.Counts;

                    // If the map is not booted, it is queued (flush at OnMapReadyAsync).
                    if (!IsMapBooted)
                        _pendingCountsUntilMap.Enqueue((msg.AntennaId, msg.Counts));
                    else
                        await UpdateAntennaMarkerLevelAsync(msg.AntennaId, msg.Counts);

                    await InvokeAsync(StateHasChanged);
                });

            _hub.On<ClientEventAntennaCrowdDTO>(
                CrowdInfoAntennaConnectionHubMethods.ToClient.EventCrowdUpdated,
                async dto =>
                {
                    _eventCrowd = dto;
                    if (dto is not null)
                    {
                        _countsByAntenna[dto.AntennaId] = dto.Counts;
                        _pendingCountsUntilMap.Enqueue((dto.AntennaId, dto.Counts));
                        if (IsMapBooted)
                        {
                            try { await UpdateAntennaMarkerLevelAsync(dto.AntennaId, dto.Counts); } catch { }
                        }
                    }
                    await InvokeAsync(StateHasChanged);
                });

            await _hub.StartAsync();
            Console.WriteLine($"✅ Connected to {hubUrl}");

            await JoinGroupsForVisibleAsync();
            await EnsureEventGroupJoinedAsync();
        }

        private async Task ResetTimerAsync()
        {
            _cts ??= new CancellationTokenSource();

            _timer?.Dispose();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, RefreshSeconds)));

            _ = Task.Run(async () =>
            {
                try
                {
                    while (_timer is not null && await _timer.WaitForNextTickAsync(_cts.Token))
                        await InvokeAsync(() => RefreshEventCrowdAsync(force: false));
                }
                catch { }
            }, _cts.Token);
        }

        private async Task RefreshEventCrowdAsync(bool force)
        {
            if (SelectedEventId is null) { _eventCrowd = null; return; }

            if (RefreshViaHub && _hub is not null && _hub.State == HubConnectionState.Connected)
            {
                try
                {
                    await EnsureEventGroupJoinedAsync();

                    await _hub.InvokeAsync(
                        CrowdInfoAntennaConnectionHubMethods.FromClient.RequestEventCrowd,
                        SelectedEventId.Value,
                        WindowMinutes,
                        MaxRadiusMeters);

                    return;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"⚠️ Hub refresh failed, fallback HTTP: {ex.Message}");
                }
            }

            _loading = true;
            try
            {
                _eventCrowd = await AntennaCrowdService.GetEventCrowdAsync(
                    SelectedEventId.Value,
                    WindowMinutes,
                    MaxRadiusMeters,
                    _cts?.Token ?? CancellationToken.None);
            }
            finally
            {
                _loading = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private async Task SyncVisibleAntennaMarkersAsync(bool fit)
        {
            if (!IsMapBooted) return;

            foreach (var a in _visibleAntennas)
            {
                if (!double.IsFinite(a.Latitude) || !double.IsFinite(a.Longitude)) continue;
                if (a.Latitude == 0 && a.Longitude == 0) continue;

                var counts = _countsByAntenna.TryGetValue(a.Id, out var c) ? c : null;
                var level = ComputeLevelByCapacity(a, counts?.ActiveConnections ?? 0);

                // ⚠️ IMPORTANT: Call the actual JS API you have: addOrUpdateAntennaMarker(antenna, scopeKey)
                // -> So send the antenna object and your scopeKey, not (id,lat,lng,level,...)
                await MapInterop.EnsureAsync();
                await MapInterop.UpsertCrowdMarkerAsync(
                    id: ANPMarkerId(a.Id),
                    lat: a.Latitude,
                    lng: a.Longitude,
                    level: level,
                    info: new { title = a.Name, description = a.Description, kind = "antenna", icon = "📡👥" },
                    scopeKey: ScopeKey
                );
            }
            if (fit)
            {
                try { await MapInterop.FitToMarkersAsync(ScopeKey); } catch { }
            }
        }
        private async Task UpdateAntennaMarkerLevelAsync(int antennaId, ClientAntennaCountsDTO counts)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            var antenna = _allAntennas.FirstOrDefault(a => a.Id == antennaId);
            if (antenna is null) return;

            var lat = antenna.Latitude;
            var lng = antenna.Longitude;
            if (!double.IsFinite(lat) || !double.IsFinite(lng) || (lat == 0 && lng == 0) ||
                lat is < -90 or > 90 || lng is < -180 or > 180)
            {
                return; // or fallback Brussels if you prefer
            }

            var active = Math.Max(0, counts?.ActiveConnections ?? 0);
            var cap = antenna.MaxCapacity.GetValueOrDefault(0);

            var level = ComputeLevelByCapacity(antenna, active);

            // warning condition : capacity reached (cap > 0)
            var isAtCapacity = cap > 0 && active >= cap;
            var isNearCapacity = cap > 0 && active >= (int)(cap * 0.9);

            var icon = isAtCapacity ? "📡🔴" : (isNearCapacity ? "📡🟠" : "📡🟢");
            level = isAtCapacity ? 4 : (isNearCapacity ? Math.Max(level, 3) : level);

            var desc = cap > 0
                ? $"{active}/{cap} connections • {counts?.UniqueDevices ?? 0} devices"
                : $"{active} connections • {counts?.UniqueDevices ?? 0} devices";

            var payload = new
            {
                Id = antenna.Id,
                Latitude = lat,
                Longitude = lng,
                Name = antenna.Name,
                Description = (isAtCapacity ? "⚠️ CAPACITY ACHIEVED • " : "") + desc,
                Level = isAtCapacity ? 4 : level,
                Icon = isAtCapacity ? "📡⚠️" : (isNearCapacity ? "📡🟠" : "📡🟢")
            };

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker", payload, ScopeKey);
        }

        private int ComputeLevelByCapacity(ClientCrowdInfoAntennaDTO antenna, int activeConnections)
        {
            var cap = ResolveCapacity(antenna);

            if (cap > 0)
            {
                var ratio = (double)activeConnections / cap;
                if (ratio <= 0.30) return 1;
                if (ratio <= 0.60) return 2;
                if (ratio <= 0.90) return 3;
                return 4;
            }

            var normalMax = GetIntCfg("OutZen:Antenna:NormalMax", 50);
            var highMax = GetIntCfg("OutZen:Antenna:HighMax", 120);
            var nearMax = GetIntCfg("OutZen:Antenna:NearCapacityMax", 200);

            if (activeConnections <= normalMax) return 1;
            if (activeConnections <= highMax) return 2;
            if (activeConnections <= nearMax) return 3;
            return 4;
        }

        private int ResolveCapacity(ClientCrowdInfoAntennaDTO antenna)
        {
            if (antenna?.MaxCapacity is > 0)
                return antenna.MaxCapacity!.Value;

            return GetIntCfg("OutZen:Antenna:DefaultCapacity", 0);
        }

        private async Task HandleScrollAsync()
        {
            // If you're not using these JS helpers, replace it with a DOM calculation via JS interop.
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (_currentIndex < ApplyFilter(_allAntennas).Count())
                {
                    await LoadMoreAndSyncAsync();
                }
            }
        }

        private int GetIntCfg(string key, int fallback)
        {
            try
            {
                var s = Config[key];
                return int.TryParse(s, out var n) ? n : fallback;
            }
            catch { return fallback; }
        }

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






































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Blazor.DTOs.Security;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
//AntennaCrowdPanelView: if no map -> scope useless; if mini-map ->scopeKey="antenna" + dedicated mapId

namespace CitizenHackathon2025V5.Blazor.Client.Pages.AntennaCrowdPanel
{
    public partial class AntennaCrowdPanel 
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

        /// <summary>
        /// If true: refresh uses SignalR Hub method RequestEventCrowd(eventId,...)
        /// If false: refresh uses HTTP GET api/crowdinfoantenna/event/{id}/crowd...
        /// </summary>
        [Parameter] public bool RefreshViaHub { get; set; } = true;

        // --- Data: antennas list + pagination ---
        public List<ClientCrowdInfoAntennaDTO> Antennas { get; set; } = new();
        private List<ClientCrowdInfoAntennaDTO> _allAntennas = new();
        private readonly List<ClientCrowdInfoAntennaDTO> _visibleAntennas = new();
        private int _currentIndex = 0;
        private const int PageSize = 25;

        // --- Event crowd (nearest antenna + counts) ---
        private ClientEventAntennaCrowdDTO _eventCrowd;
        private bool _loading;

        // --- Leaflet module ---
        private IJSObjectReference _outzen;
        private bool _booted;
        private bool _mapInitStarted;

        // --- SignalR ---
        private HubConnection _hub;
        private readonly ConcurrentQueue<(int AntennaId, ClientAntennaCountsDTO Counts)> _pendingCountsUntilMap = new();
        private readonly Dictionary<int, ClientAntennaCountsDTO> _countsByAntenna = new();
        private readonly HashSet<int> _joinedAntennaGroups = new();
        private int? _joinedEventGroup;

        // --- Timer ---
        private PeriodicTimer _timer;
        private CancellationTokenSource _cts;

        // --- UI refs (if you use them in razor) ---
        private ElementReference ScrollContainerRef;
        private ElementReference TableScrollRef;

        // --- Filtering/search (optional, still clean) ---
        private string _q = string.Empty;

        // ----------------------------
        // Lifecycle
        // ----------------------------
        protected override bool MapEnabled => false;

        // Required (contract), but unused if MapEnabled=false
        protected override string ScopeKey => "antenna";
        protected override string MapId => "outzenMap_antenna";
        protected override async Task OnInitializedAsync()
        {
            await LoadAntennasAsync();
            await StartSignalRAsync();
        }

        protected override async Task OnParametersSetAsync()
        {
            await RefreshEventCrowdAsync(force: true);
            await ResetTimerAsync();
            await EnsureEventGroupJoinedAsync();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || _booted || _mapInitStarted) return;
            _mapInitStarted = true;

            // Wait container exists
            for (var i = 0; i < 10; i++)
            {
                if (await JS.InvokeAsync<bool>("checkElementExists", "leafletMap")) break;
                await Task.Delay(150);
                if (i == 9) return;
            }

            _outzen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            var ok = await _outzen.InvokeAsync<bool>("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new[] { 50.89, 4.34 },
                zoom = 13,
                enableChart = false,
                force = true
            });

            if (!ok) return;

            // basic readiness (if your module has isOutZenReady)
            bool ready = false;
            for (var i = 0; i < 25; i++)
            {
                try { ready = await _outzen.InvokeAsync<bool>("isOutZenReady"); } catch { }
                if (ready) break;
                await Task.Delay(120);
            }
            if (!ready) return;

            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }

            // Seed markers with currently visible antennas
            await SyncVisibleAntennaMarkersAsync(fit: true);

            // Flush pending counts updates received before map boot
            while (_pendingCountsUntilMap.TryDequeue(out var item))
            {
                await UpdateAntennaMarkerLevelAsync(item.AntennaId, item.Counts);
            }

            _booted = true;
            await InvokeAsync(StateHasChanged);
        }

        // ----------------------------
        // Antennas loading + pagination
        // ----------------------------
        private async Task LoadAntennasAsync()
        {
            try
            {
                var fetched = await AntennaService.GetAllAsync(_cts?.Token ?? CancellationToken.None);
                Antennas = fetched ?? new List<ClientCrowdInfoAntennaDTO>();

                _allAntennas = Antennas
                    .Where(a => a is not null)
                    .OrderByDescending(a => a.Id)
                    .ToList();

                _visibleAntennas.Clear();
                _currentIndex = 0;
                LoadMoreItems();

                // Join groups for the initial visible page
                await JoinGroupsForVisibleAsync();

                await InvokeAsync(StateHasChanged);
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

        /// <summary>
        /// Called by your scroll handler when user hits bottom.
        /// Adds next page + joins antenna groups for new antennas + sync markers.
        /// </summary>
        private async Task LoadMoreAndSyncAsync()
        {
            var before = _visibleAntennas.Select(a => a.Id).ToHashSet();
            LoadMoreItems();
            var added = _visibleAntennas.Where(a => !before.Contains(a.Id)).ToList();

            await JoinGroupsAsync(added.Select(a => a.Id));
            await SyncVisibleAntennaMarkersAsync(fit: false);

            await InvokeAsync(StateHasChanged);
        }

        // ----------------------------
        // Join/Leave groups when scrolling
        // ----------------------------

        /// <summary>
        /// Join groups for all antennas in _visibleAntennas (initial page).
        /// </summary>
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

        private async Task LeaveGroupsAsync(IEnumerable<int> antennaIds)
        {
            if (_hub is null) return;

            foreach (var id in antennaIds.Distinct())
            {
                if (!_joinedAntennaGroups.Contains(id)) continue;
                try
                {
                    await _hub.InvokeAsync(CrowdInfoAntennaConnectionHubMethods.FromClient.LeaveAntenna, id);
                    _joinedAntennaGroups.Remove(id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"❌ LeaveAntenna({id}) failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Optional optimization: keep only groups for visible antennas.
        /// Call this if you implement "virtualization" where visible set changes a lot.
        /// </summary>
        private async Task NormalizeJoinedGroupsToVisibleAsync()
        {
            var visibleIds = _visibleAntennas.Select(a => a.Id).ToHashSet();
            var toLeave = _joinedAntennaGroups.Where(id => !visibleIds.Contains(id)).ToList();
            var toJoin = visibleIds.Where(id => !_joinedAntennaGroups.Contains(id)).ToList();

            await LeaveGroupsAsync(toLeave);
            await JoinGroupsAsync(toJoin);
        }

        private async Task EnsureEventGroupJoinedAsync()
        {
            if (_hub is null) return;

            var desired = SelectedEventId;

            // leave previous
            if (_joinedEventGroup is not null && _joinedEventGroup != desired)
            {
                try { await _hub.InvokeAsync(CrowdInfoAntennaConnectionHubMethods.FromClient.LeaveEvent, _joinedEventGroup.Value); }
                catch { }
                _joinedEventGroup = null;
            }

            // join new
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

        // ----------------------------
        // SignalR setup + handlers
        // ----------------------------
        private async Task StartSignalRAsync()
        {
            var hubUrl = HubUrls.Build(HubPaths.AntennaConnection);

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // counts per antenna updates
            _hub.On<ClientAntennaCountsUpdateDTO>(
                CrowdInfoAntennaConnectionHubMethods.ToClient.AntennaCountsUpdated,
                async msg =>
                {
                    _countsByAntenna[msg.AntennaId] = msg.Counts;

                    if (!_booted || _outzen is null)
                        _pendingCountsUntilMap.Enqueue((msg.AntennaId, msg.Counts));
                    else
                        await UpdateAntennaMarkerLevelAsync(msg.AntennaId, msg.Counts);

                    await InvokeAsync(StateHasChanged);
                });

            // event crowd updates (event -> nearest antenna -> counts)
            _hub.On<ClientEventAntennaCrowdDTO>(
                CrowdInfoAntennaConnectionHubMethods.ToClient.EventCrowdUpdated,
                async dto =>
                {
                    _eventCrowd = dto;

                    // reflect marker change for the nearest antenna
                    if (dto is not null)
                    {
                        _countsByAntenna[dto.AntennaId] = dto.Counts;

                        if (!_booted || _outzen is null)
                            _pendingCountsUntilMap.Enqueue((dto.AntennaId, dto.Counts));
                        else
                            await UpdateAntennaMarkerLevelAsync(dto.AntennaId, dto.Counts);
                    }

                    await InvokeAsync(StateHasChanged);
                });

            await _hub.StartAsync();
            Console.WriteLine($"✅ Connected to {hubUrl}");

            // join initial visible antenna groups + current event group
            await JoinGroupsForVisibleAsync();
            await EnsureEventGroupJoinedAsync();
        }

        // ----------------------------
        // Refresh strategy: Hub push vs HTTP pull
        // ----------------------------
        private async Task ResetTimerAsync()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = new CancellationTokenSource();

            _timer?.Dispose();
            _timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, RefreshSeconds)));

            _ = Task.Run(async () =>
            {
                try
                {
                    while (_timer is not null && await _timer.WaitForNextTickAsync(_cts.Token))
                    {
                        await InvokeAsync(() => RefreshEventCrowdAsync(force: false));
                    }
                }
                catch { /* ignore */ }
            }, _cts.Token);
        }

        private async Task RefreshEventCrowdAsync(bool force)
        {
            if (SelectedEventId is null) { _eventCrowd = null; return; }

            // If using hub: call RequestEventCrowd -> server replies via EventCrowdUpdated
            if (RefreshViaHub && _hub is not null && _hub.State == HubConnectionState.Connected)
            {
                try
                {
                    // Avoid spamming if not forced and no event group joined
                    await EnsureEventGroupJoinedAsync();

                    await _hub.InvokeAsync(
                        CrowdInfoAntennaConnectionHubMethods.FromClient.RequestEventCrowd, // <-- see note below
                        SelectedEventId.Value,
                        WindowMinutes,
                        MaxRadiusMeters);

                    return;
                }
                catch (Exception ex)
                {
                    // fallback to HTTP if hub fails
                    Console.Error.WriteLine($"⚠️ Hub refresh failed, fallback HTTP: {ex.Message}");
                }
            }

            // HTTP fallback/pull
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

        // NOTE:
        // Your Contracts class CrowdInfoAntennaConnectionHubMethods currently does NOT include "RequestEventCrowd".
        // Add it there (FromClient) with const string RequestEventCrowd = "RequestEventCrowd";
        // otherwise replace the InvokeAsync string with the raw method name.

        // ----------------------------
        // Leaflet markers sync + color by capacity
        // ----------------------------
        private async Task SyncVisibleAntennaMarkersAsync(bool fit)
        {
            if (_outzen is null) return;

            foreach (var a in _visibleAntennas)
            {
                if (!double.IsFinite(a.Latitude) || !double.IsFinite(a.Longitude)) continue;
                if (a.Latitude == 0 && a.Longitude == 0) continue;

                var counts = _countsByAntenna.TryGetValue(a.Id, out var c) ? c : null;
                var level = ComputeLevelByCapacity(a, counts?.ActiveConnections ?? 0);

                var markerId = $"ant:{a.Id}";
                var title = a.Name ?? $"Antenne {a.Id}";
                var desc = counts is null
                    ? "Aucune donnée récente"
                    : $"{counts.ActiveConnections} connexions • {counts.UniqueDevices} devices";

                await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateAntennaMarker",
                    markerId,
                    a.Latitude,
                    a.Longitude,
                    level,
                    new { title, description = desc });
            }

            if (fit && _visibleAntennas.Count > 0)
            {
                try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }
            }
        }

        private async Task UpdateAntennaMarkerLevelAsync(int antennaId, ClientAntennaCountsDTO counts)
        {
            if (_outzen is null) return;

            var antenna = _allAntennas.FirstOrDefault(a => a.Id == antennaId);
            var level = ComputeLevelByCapacity(antenna, counts.ActiveConnections);

            await _outzen.InvokeVoidAsync("setAntennaMarkerLevel",
                $"ant:{antennaId}",
                level,
                new { title = antenna?.Name ?? $"Antenne {antennaId}", description = $"{counts.ActiveConnections} connexions" });
        }

        /// <summary>
        /// Computes the severity 1..4 based on antenna capacity.
        /// - Green: <= 30% capacity
        /// - Orange: <= 60%
        /// - Red: <= 90%
        /// - Bright red: > 90%
        /// 
        /// If no capacity known, fallback to config thresholds Normal/High/NearCapacity.
        /// </summary>
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

            // Fallback: thresholds from config or defaults
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
            // MaxCapacity est typiquement int? (nullable)
            if (antenna?.MaxCapacity is > 0)
                return antenna.MaxCapacity!.Value;

            return GetIntCfg("OutZen:Antenna:DefaultCapacity", 0);
        }

        private int GetIntCfg(string key, int fallback)
        {
            try
            {
                var s = Config[key];
                return int.TryParse(s, out var n) ? n : fallback;
            }
            catch
            {
                return fallback;
            }
        }
        // ----------------------------
        // Scroll handler hook (call from razor)
        // ----------------------------
        private async Task HandleScrollAsync()
        {
            // Your JS functions must exist:
            // getScrollTop, getScrollHeight, getClientHeight
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", TableScrollRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                // If still more to load
                if (_currentIndex < ApplyFilter(_allAntennas).Count())
                {
                    await LoadMoreAndSyncAsync();

                    // Optional: normalize joined groups to visible (if you also remove items)
                    // await NormalizeJoinedGroupsToVisibleAsync();
                }
            }
        }

        // ----------------------------
        // Disposal
        // ----------------------------
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _timer?.Dispose();
        }
    }
}





































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Shared;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoView : IAsyncDisposable
    {
#nullable disable
        [Inject] public CrowdInfoService CrowdInfoService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        private const string ApiBase = "https://localhost:7254";

        private HubConnection hubConnection;

        // === Data ===
        public List<ClientCrowdInfoDTO> CrowdInfos { get; set; } = new();
        private List<ClientCrowdInfoDTO> allCrowdInfos = new();
        private List<ClientCrowdInfoDTO> visibleCrowdInfos = new();
        private IJSObjectReference _outzen;
        private int currentIndex = 0;
        private const int PageSize = 25;
        private const int MaxBootRetries = 25;

        // === UI & Scroll ===
        private ElementReference ScrollContainerRef;
        private ElementReference TableScrollRef;
        private string _q = string.Empty;
        private bool _onlyRecent;
        private bool _booted;

        // === Earth canvas ids (if used elsewhere) ===
        private readonly string _speedId = $"speedRange-{Guid.NewGuid():N}";
        private readonly Dictionary<int, int> _lastLevels = new();
        private bool _initialDataApplied = false;

        // === SignalR buffering until map ready ===
        private readonly ConcurrentQueue<ClientCrowdInfoDTO> _pendingHubUpdates = new();

        public int SelectedId { get; set; }

        protected override async Task OnInitializedAsync()
        {
            try
            {
                var fetched = (await CrowdInfoService.GetAllCrowdInfoAsync()).ToList();
                CrowdInfos = fetched;
                allCrowdInfos = fetched;
                visibleCrowdInfos.Clear();
                currentIndex = 0;
                Console.WriteLine($"[CrowdInfoView] fetched={fetched.Count}");
                Console.WriteLine($"[CrowdInfoView] visible={visibleCrowdInfos.Count} all={allCrowdInfos.Count}");
                LoadMoreItems();

                foreach (var co in fetched)
                    _lastLevels[co.Id] = co.CrowdLevel;

                await InvokeAsync(StateHasChanged);

                // Hub SignalR
                var hubUrl = HubUrls.Build(HubPaths.CrowdInfo);

                hubConnection = new HubConnectionBuilder()
                    .WithUrl(hubUrl, options =>
                    {
                        options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                    })
                    .WithAutomaticReconnect()
                    .Build();

                RegisterHubHandlers();
                await hubConnection.StartAsync();
                Console.WriteLine($"✅ Connected to {hubUrl}");

                // 🔁 Catch-up: if the map is already ready, we push the markers now
                if (_booted && _outzen is not null && visibleCrowdInfos.Any())
                {
                    Console.WriteLine($"[CrowdInfoView] Data loaded after map. Syncing {FilterCrowd(visibleCrowdInfos).Count()} markers.");
                    await SyncMapMarkersAsync(fit: true);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Init error: {ex.Message}");
            }
        }

        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;

            hubConnection.On<ClientCrowdInfoDTO>(CitizenHackathon2025.Contracts.Hubs.CrowdHubMethods.ToClient.ReceiveCrowdUpdate, async dto =>
            {
                UpsertLocal(dto);

                int prev = _lastLevels.TryGetValue(dto.Id, out var p) ? p : 0;
                _lastLevels[dto.Id] = dto.CrowdLevel;

                if (_initialDataApplied && prev < 4 && dto.CrowdLevel == 4)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.beepCritical", dto.Id); } catch { }
                }

                if (!_booted || _outzen is null)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                var lvl = Math.Clamp(dto.CrowdLevel, 1, SharedConstants.MaxCrowdLevel);

                await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, lvl,
                    new { title = dto.LocationName, description = $"Maj {dto.Timestamp:HH:mm:ss}" });

                try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>(CitizenHackathon2025.Contracts.Hubs.CrowdHubMethods.ToClient.CrowdInfoArchived, async id =>
            {
                CrowdInfos.RemoveAll(c => c.Id == id);
                allCrowdInfos.RemoveAll(c => c.Id == id);
                visibleCrowdInfos.RemoveAll(c => c.Id == id);

                if (_booted && _outzen is not null)
                {
                    await _outzen.InvokeVoidAsync("removeCrowdMarker", id.ToString());
                    await SyncMapMarkersAsync(fit: false);
                }

                await InvokeAsync(StateHasChanged);
            });
        }
        private void UpsertLocal(ClientCrowdInfoDTO dto)
        {
            void Upsert(List<ClientCrowdInfoDTO> list)
            {
                var i = list.FindIndex(c => c.Id == dto.Id);
                if (i >= 0) list[i] = dto; else list.Add(dto);
            }
            Upsert(CrowdInfos);
            Upsert(allCrowdInfos);

            var j = visibleCrowdInfos.FindIndex(c => c.Id == dto.Id);
            if (j >= 0) visibleCrowdInfos[j] = dto;
        }

        private bool _mapInitStarted = false;

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || _booted || _mapInitStarted) return;
            _mapInitStarted = true;

            // 0) Wait container exists
            for (var i = 0; i < 10; i++)
            {
                if (await JS.InvokeAsync<bool>("checkElementExists", "leafletMap")) break;
                await Task.Delay(150);
                if (i == 9) { Console.WriteLine("❌ leafletMap not found."); return; }
            }

            _outzen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            var ok = await _outzen.InvokeAsync<bool>("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new[] { 50.89, 4.34 },
                zoom = 13,
                enableChart = true,
                force = true
            });

            if (!ok) return;

            // Readiness check (no custom isOutZenReady required)
            bool ready = false;
            for (var i = 0; i < 25; i++)
            {
                try { ready = await _outzen.InvokeAsync<bool>("isOutZenReady"); } catch { }
                if (ready) break;
                await Task.Delay(120);
            }
            if (!ready) return;

            string current = null;
            for (var i = 0; i < 20; i++)
            {
                try { current = await _outzen.InvokeAsync<string>("getCurrentMapId"); } catch { }
                if (!string.IsNullOrWhiteSpace(current)) break;
                await Task.Delay(150);
            }
            if (string.IsNullOrWhiteSpace(current)) { Console.WriteLine("❌ map not ready"); return; }

            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }

            // Seed from current visible data
            await SyncMapMarkersAsync(fit: true);

            // Flush buffered hub updates
            while (_pendingHubUpdates.TryDequeue(out var dto))
            {
                if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) continue;
                if (dto.Latitude == 0 && dto.Longitude == 0) continue;

                var lvl = Math.Clamp(dto.CrowdLevel, 1, SharedConstants.MaxCrowdLevel);
                await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    $"cr:{dto.Id}", dto.Latitude, dto.Longitude, lvl,
                    new { title = dto.LocationName, description = $"Maj {dto.Timestamp:HH:mm:ss}", icon = "👥" });
            }

            try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }

            _initialDataApplied = true;
            _booted = true;
        }

        private async Task SyncMapMarkersAsync(bool fit = true)
        {
            try
            {
                var items = FilterCrowd(visibleCrowdInfos).ToList();
                Console.WriteLine($"[CrowdInfoView] SyncMapMarkersAsync: {items.Count} markers.");

                await _outzen.InvokeVoidAsync("clearCrowdMarkers");

                foreach (var co in items)
                {
                    if (!double.IsFinite(co.Latitude) || !double.IsFinite(co.Longitude)) continue;
                    if (co.Latitude == 0 && co.Longitude == 0) continue;

                    var lvl = Math.Clamp(co.CrowdLevel, 1, SharedConstants.MaxCrowdLevel);

                    await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                        $"cr:{co.Id}", co.Latitude, co.Longitude, lvl,
                        new { title = co.LocationName, description = $"Maj {co.Timestamp:HH:mm:ss}", icon = "👥" });
                }

                if (fit && items.Any())
                {
                    try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }
                }
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ JSInterop failed: {jsex.Message}");
            }
        }
        private void LoadMoreItems()
        {
            var next = allCrowdInfos.Skip(currentIndex).Take(PageSize).ToList();
            visibleCrowdInfos.AddRange(next);
            currentIndex += next.Count;
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
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", TableScrollRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allCrowdInfos.Count)
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

        private string Q
        {
            get => _q;
            set { _q = value; _ = SyncMapMarkersAsync(fit: true); }
        }

        private static string InfoDesc(ClientCrowdInfoDTO co)
            => CrowdInfoSeverityHelpers.GetDescription(
                CrowdInfoSeverityHelpers.GetSeverity(co));

        private static string GetLevelCss(int level)
        {
            var safe = Math.Clamp(level, 0, 5);
            return $"info--lvl{safe}";
        }

        private void ClickInfo(int id) => SelectedId = id;

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_outzen is not null)
                {
                    try { await _outzen.InvokeVoidAsync("disposeOutZen", new { mapId = "leafletMap" }); } catch { }
                    await _outzen.DisposeAsync();
                }
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





using CitizenHackathon2025V5.Blazor.Client.DTOs;
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

        private const string ApiBase = "https://localhost:7254";

        private HubConnection? hubConnection;

        // === Data ===
        public List<ClientCrowdInfoDTO> CrowdInfos { get; set; } = new();
        private List<ClientCrowdInfoDTO> allCrowdInfos = new();
        private List<ClientCrowdInfoDTO> visibleCrowdInfos = new();
        private IJSObjectReference? _outzen;
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
        private readonly string _canvasId = $"rotatingEarth-{Guid.NewGuid():N}";
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
                LoadMoreItems();

                // Baseline levels (no beep on loading)
                foreach (var co in fetched)
                    _lastLevels[co.Id] = co.CrowdLevel;

                await InvokeAsync(StateHasChanged);

                // Hub SignalR
                var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase;
                var hubUrl = $"{apiBaseUrl}/hubs/crowdHub";

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
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Init error: {ex.Message}");
            }
        }

        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;

            hubConnection.On<ClientCrowdInfoDTO>("ReceiveCrowdUpdate", async dto =>
            {
                UpsertLocal(dto);

                int prev = _lastLevels.TryGetValue(dto.Id, out var p) ? p : 0;
                _lastLevels[dto.Id] = dto.CrowdLevel;

                // Beep only if the app is ready and we're moving to 4
                if (_initialDataApplied && prev < 4 && dto.CrowdLevel == 4)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.beepCritical", dto.Id); } catch { }
                }

                bool interopReady = false;
                try { interopReady = await JS.InvokeAsync<bool>("isOutZenReady"); } catch { }

                if (!interopReady)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, dto.CrowdLevel,
                    new { title = dto.LocationName, description = $"Maj {dto.Timestamp:HH:mm:ss}" });

                try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("CrowdInfoArchived", async id =>
            {
                CrowdInfos.RemoveAll(c => c.Id == id);
                allCrowdInfos.RemoveAll(c => c.Id == id);
                visibleCrowdInfos.RemoveAll(c => c.Id == id);

                bool interopReady = false;
                try { interopReady = await JS.InvokeAsync<bool>("isOutZenReady"); } catch { }

                if (interopReady)
                {
                    await _outzen.InvokeVoidAsync("removeCrowdMarker", id.ToString());
                }

                await SyncMapMarkersAsync(fit: false);
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

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender || _booted) return;

            // 0) Waiting for the container
            for (var i = 0; i < 10; i++)
            {
                var ok = await JS.InvokeAsync<bool>("checkElementExists", "leafletMap");
                if (ok) break;
                await Task.Delay(150);
                if (i == 9) { Console.WriteLine("❌ Map container not found."); return; }
            }

            // 1) Import ESM module
            _outzen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            // 2) Boot if necessary
            bool ready = false;
            for (var i = 0; i < 40; i++)
            {
                try
                {
                    ready = await _outzen.InvokeAsync<bool>("isOutZenReady");
                    if (ready) break;
                    await _outzen.InvokeVoidAsync("bootOutZen", new
                    {
                        mapId = "leafletMap",
                        center = new[] { 50.89, 4.34 },
                        zoom = 13,
                        enableChart = true,
                        force = false
                    });
                    await Task.Delay(200);
                }
                catch { }
            }

            // 3) Final check
            if (!ready)
            {
                Console.WriteLine("❌ OutZen not ready after retries.");
                return;
            }

            Console.WriteLine("✅ OutZen ready, initializing map markers...");

            // 4) Refresh the map
            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }

            // 5) Add the initial markers
            await SyncMapMarkersAsync(fit: true);

            // 6) Flush the SignalR queue (if received before map)
            while (_pendingHubUpdates.TryDequeue(out var dto))
            {
                var lvl = Math.Clamp(dto.CrowdLevel, 1, SharedConstants.MaxCrowdLevel);
                await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, lvl,
                    new { title = dto.LocationName, description = $"Maj {dto.Timestamp:HH:mm:ss}" });
            }

            // 7) One-time global fit
            try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { }

            _initialDataApplied = true;
            _booted = true;
        }



        private async Task SyncMapMarkersAsync(bool fit = true)
        {
            try
            {
                await _outzen.InvokeVoidAsync("clearCrowdMarkers");

                foreach (var co in FilterCrowd(visibleCrowdInfos))
                {
                    var lvl = Math.Clamp(co.CrowdLevel, 1, SharedConstants.MaxCrowdLevel);
                    await _outzen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                        co.Id.ToString(), co.Latitude, co.Longitude, lvl,
                        new { title = co.LocationName, description = $"Maj {co.Timestamp:HH:mm:ss}" });
                    Console.WriteLine($"First: {co.Id} -> {co.Latitude},{co.Longitude}");
                }

                if (fit && visibleCrowdInfos.Any()) { try { await _outzen.InvokeVoidAsync("fitToMarkers"); } catch { } }
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
                await JS.InvokeVoidAsync("disposeEarth", _canvasId);
            }
            catch { /* ignore errors during disposal */ }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}














































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





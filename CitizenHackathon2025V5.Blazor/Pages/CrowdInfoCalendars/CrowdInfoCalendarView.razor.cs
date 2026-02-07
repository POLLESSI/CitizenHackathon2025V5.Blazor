//CrowdInfoCalendarView.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Shared;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;
using static CitizenHackathon2025V5.Blazor.Client.Services.CrowdInfoCalendarService;
//CrowdInfoCalendarView: scopeKey = "calendar"

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarView : IAsyncDisposable
    {
        [Inject] public CrowdInfoCalendarService CrowdInfoCalendarService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        private const string ApiBase = "https://localhost:7254";

        private HubConnection? hubConnection;

        // === Data ===
        public List<ClientCrowdInfoCalendarDTO> CrowdInfoCalendars { get; set; } = new();
        private List<ClientCrowdInfoCalendarDTO> allCrowdInfoCalendars = new();
        private List<ClientCrowdInfoCalendarDTO> VisibleCrowdInfoCalendars = new();

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
        private DateTime? from;
        private DateTime? to;
        private string? region;
        private int? placeId;
        private string? activeFilter; // "", "true", "false"
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";
        private readonly Dictionary<int, int> _lastLevels = new();
        private bool _initialDataApplied = false;

        // === SignalR buffering until map ready ===
        private readonly ConcurrentQueue<ClientCrowdInfoCalendarDTO> _pendingHubUpdates = new();
        public int SelectedId { get; set; }

        protected override async Task OnInitializedAsync()
        {
            //var exists = await JS.InvokeAsync<bool>("checkElementExists", "leafletMap");
            try
            {
                var fetched = (await CrowdInfoCalendarService.GetAllSafeAsync()).ToList();
                CrowdInfoCalendars = fetched;
                allCrowdInfoCalendars = fetched;
                VisibleCrowdInfoCalendars.Clear();
                currentIndex = 0;
                LoadMoreItems();
               
                foreach (var co in fetched)
                    _lastLevels[co.Id] = co.ExpectedLevel.GetValueOrDefault();

                //await InvokeAsync(StateHasChanged);

                // Hub SignalR
                var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase;
                var hubUrl = $"{apiBaseUrl}/hubs/{CrowdCalendarHubMethods.HubPath}";

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
                if (_booted && _outzen is not null && VisibleCrowdInfoCalendars.Any())
                {
                    Console.WriteLine($"[CrowdInfoCalendarView] Data loaded after map. Syncing {FilterCrowdCalendar(VisibleCrowdInfoCalendars).Count()} markers.");
                    await SyncMapMarkersAsync(fit: true);
                }
            }
            catch (Exception ex)
            {

                Console.Error.WriteLine($"❌ Init error: {ex.Message}");
            }
            await InvokeAsync(StateHasChanged);
        }
        private void RegisterHubHandlers()
        {
            if (hubConnection is null) return;

            hubConnection.On<ClientCrowdInfoCalendarDTO>("ReceiveCrowdCalendarUpdate", async dto =>
            {
                UpsertLocal(dto);

                int prev = _lastLevels.TryGetValue(dto.Id, out var p) ? p : 0;
                int next = dto.ExpectedLevel.GetValueOrDefault();
                _lastLevels[dto.Id] = next;

                // beep si tu veux sur ExpectedLevel (ou autre logique)
                if (_initialDataApplied && prev < 4 && next == 4)
                {
                    try { await JS.InvokeVoidAsync("OutZenInterop.beepCritical", dto.Id); } catch { }
                }

                if (!_booted || _outzen is null)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                var lvl = Math.Clamp(next, 1, SharedConstants.MaxCrowdLevel);

                await _outzen.InvokeVoidAsync("addOrUpdateCrowdCalendarMarker",
                    $"cc:{dto.Id}", dto.Latitude, dto.Longitude, lvl,
                    new { title = dto.EventName, description = $"Maj {dto.DateUtc:HH:mm:ss}", icon = "🥁🎉" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("CrowdInfoArchived", async id =>
            {
                CrowdInfoCalendars.RemoveAll(c => c.Id == id);
                allCrowdInfoCalendars.RemoveAll(c => c.Id == id);
                VisibleCrowdInfoCalendars.RemoveAll(c => c.Id == id);

                if (_booted && _outzen is not null)
                {
                    await _outzen.InvokeVoidAsync("removeCrowdCalendarMarker", $"cc:{id}");
                    await SyncMapMarkersAsync(fit: false);
                }

                await InvokeAsync(StateHasChanged);
            });
        }
        private void UpsertLocal(ClientCrowdInfoCalendarDTO dto)
        {
            void Upsert(List<ClientCrowdInfoCalendarDTO> list)
            {
                var i = list.FindIndex(c => c.Id == dto.Id);
                if (i >= 0) list[i] = dto; else list.Add(dto);
            }
            Upsert(CrowdInfoCalendars);
            Upsert(allCrowdInfoCalendars);

            var j = VisibleCrowdInfoCalendars.FindIndex(c => c.Id == dto.Id);
            if (j >= 0) VisibleCrowdInfoCalendars[j] = dto;
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

            if (!ok) { Console.WriteLine("❌ bootOutZen failed"); return; }

            try { await _outzen.InvokeVoidAsync("enableHybridZoom", false, 13); } catch { }
            // Readiness check (no custom isOutZenReady required)
            string? current = null;
            for (var i = 0; i < 20; i++)
            {
                try { current = await _outzen.InvokeAsync<string>("getCurrentMapId"); } catch { }
                if (!string.IsNullOrWhiteSpace(current)) break;
                await Task.Delay(150);
            }
            if (string.IsNullOrWhiteSpace(current)) { Console.WriteLine("❌ map not ready"); return; }

            try { await _outzen.InvokeVoidAsync("refreshMapSize"); } catch { }

            if (!VisibleCrowdInfoCalendars.Any())
            {
                Console.WriteLine("[CrowdInfoCalendarView] Visible is empty, rebuilding from all...");
                VisibleCrowdInfoCalendars.Clear();
                currentIndex = 0;
                LoadMoreItems();
            }
            // Seed from current visible data
            await SyncMapMarkersAsync(fit: true);

            // Flush buffered hub updates
            while (_pendingHubUpdates.TryDequeue(out var dto))
            {
                if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) continue;
                if (dto.Latitude == 0 && dto.Longitude == 0) continue;

                int lvlInt = Math.Clamp(dto.ExpectedLevel.GetValueOrDefault(), 1, SharedConstants.MaxCrowdLevel);
                byte lvlByte = (byte)lvlInt;
                await _outzen.InvokeVoidAsync("addOrUpdateCrowdCalendarMarker",
                    $"cc:{dto.Id}", dto.Latitude, dto.Longitude, lvlInt,
                    new { title = dto.EventName, description = $"Maj {dto.DateUtc:HH:mm:ss}", icon = "🥁🎉" });
            }

            try { await _outzen.InvokeVoidAsync("fitToCalendarMarkers"); } catch { }

            _initialDataApplied = true;
            _booted = true;
        }

        private async Task SyncMapMarkersAsync(bool fit = true)
        {
            if (_outzen is null) return;
            try
            {
                var items = FilterCrowdCalendar(VisibleCrowdInfoCalendars).ToList();
                Console.WriteLine($"[CrowdInfoCalendarView] SyncMapMarkersAsync: {items.Count} markers.");

                Console.WriteLine("[Calendar] calling clearCrowdCalendarMarkers");
                await _outzen.InvokeVoidAsync("clearCrowdCalendarMarkers");

                foreach (var co in items)
                {
                    if (!double.IsFinite(co.Latitude) || !double.IsFinite(co.Longitude)) continue;
                    if (co.Latitude == 0 && co.Longitude == 0) continue;

                    var lvl = Math.Clamp(co.ExpectedLevel.GetValueOrDefault(), 1, SharedConstants.MaxCrowdLevel);

                    await _outzen.InvokeVoidAsync("addOrUpdateCrowdCalendarMarker",
                        $"cc:{co.Id}", co.Latitude, co.Longitude, lvl,
                        new { title = co.EventName, description = $"Maj {co.DateUtc:HH:mm:ss}", icon = "🥁🎉" });

                    Console.WriteLine($"[CrowdInfoCalendarView] Marker: {co.Id} -> {co.Latitude},{co.Longitude}");
                }

                if (fit && items.Any())
                {
                    try { await _outzen.InvokeVoidAsync("fitToCalendarMarkers"); } catch { }
                }
                Console.WriteLine($"[Calendar] after filter -> {items.Count} (onlyRecent={_onlyRecent}, q='{_q}')");
            }
            catch (JSException jsex)
            {
                Console.Error.WriteLine($"❌ JSInterop failed: {jsex.Message}");
            }
        }

        private void GoNew() => Nav.NavigateTo("/crowdcalendar/new");
        private void GoDetail(int id) => Nav.NavigateTo($"/crowdcalendar/{id}");

        private async Task Load()
        {
            bool? active = activeFilter switch { "true" => true, "false" => false, _ => (bool?)null };

            // ✅ Safe-null + empty list
            allCrowdInfoCalendars = (await Svc.ListAsync(from, to, region, placeId, active))?.ToList() ?? new List<ClientCrowdInfoCalendarDTO>();
        }

        private async Task LoadAll()
        {
            var fetched = await CrowdInfoCalendarService.GetAllSafeAsync();
            CrowdInfoCalendars = fetched;
            allCrowdInfoCalendars = fetched;
            VisibleCrowdInfoCalendars.Clear();
            currentIndex = 0;
            LoadMoreItems();
        }

        private void LoadMoreItems()
        {
            var next = allCrowdInfoCalendars.Skip(currentIndex).Take(PageSize).ToList();
            VisibleCrowdInfoCalendars.AddRange(next);
            currentIndex += next.Count;
            Console.WriteLine($"[Calendar] LoadMoreItems -> added {next.Count}, visible={VisibleCrowdInfoCalendars.Count}, all={allCrowdInfoCalendars.Count}");
        }

        private IEnumerable<ClientCrowdInfoCalendarDTO> FilterCrowdCalendar(IEnumerable<ClientCrowdInfoCalendarDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            Console.WriteLine($"[Filter] q='{_q}', onlyRecent={_onlyRecent}, totalVisible={source.Count()}");
            return source
                .Where(x =>
                    string.IsNullOrEmpty(q) ||
                    (x.EventName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Latitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Longitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.DateUtc >= cutoff);
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", TableScrollRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5 && currentIndex < allCrowdInfoCalendars.Count)
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

        private static string InfoDescCalendar(ClientCrowdInfoCalendarDTO co)
             => CrowdInfoSeverityHelpers.GetDescription(CrowdInfoSeverityHelpers.GetSeverity(co));

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



































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/
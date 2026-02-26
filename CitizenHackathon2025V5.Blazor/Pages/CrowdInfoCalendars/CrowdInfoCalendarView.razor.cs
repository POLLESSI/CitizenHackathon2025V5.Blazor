// CrowdInfoCalendarView.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
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

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarView : OutZenMapPageBase
    {
#nullable disable
        // ===== Inject =====
        [Inject] public CrowdInfoCalendarService CrowdInfoCalendarService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!; // If you already have it, otherwise delete it and keep the manual build.

        //private const string ApiBase = "https://localhost:7254";

        // ===== OutZen contract =====
        protected override string ScopeKey => "crowdinfocalendarview";

        // ⚠️ MPORTANT: correct your typo: "crowdinfocaledarview" -> "crowdinfocaledarview"
        protected override string MapId => "leafletMap-crowdinfocalendarview";

        // boot options
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // Optionnel
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        // ===== SignalR =====
        private HubConnection _hub;

        // ===== Search form fields (used by .razor) =====
        private DateTime from = DateTime.UtcNow.Date.AddDays(-7);
        private DateTime to = DateTime.UtcNow.Date.AddDays(+7);
        private string region = "";
        private int? placeId = null;
        private long _lastFitTicks;
        private static string CICMarkerId(int id) => $"cc:{id}";
        private string activeFilter = ""; // "", "true", "false"

        // Razor expects this exact name:
        private List<ClientCrowdInfoCalendarDTO> allCrowdInfoCalendars;

        // ===== Data =====
        public List<ClientCrowdInfoCalendarDTO> CrowdInfoCalendars { get; set; } = new();
        private List<ClientCrowdInfoCalendarDTO> _all = new();
        private readonly List<ClientCrowdInfoCalendarDTO> _visible = new();

        private int _currentIndex = 0;
        private const int PageSize = 25;

        // ===== UI / filtering =====
        private ElementReference ScrollContainerRef;
        private ElementReference TableScrollRef;
        private string _q = string.Empty;
        private bool _onlyRecent;

        // ===== Map state =====
        private bool _disposed;

        // ===== Hub buffering until map ready =====
        private readonly ConcurrentQueue<ClientCrowdInfoCalendarDTO> _pendingHubUpdates = new();
        private readonly Dictionary<int, int> _lastLevels = new();

        public int SelectedId { get; set; }

        // ----------------------------
        // Lifecycle
        // ----------------------------
        protected override async Task OnInitializedAsync()
        {
            await LoadAllAsync();
            allCrowdInfoCalendars = _all;
            await StartSignalRAsync();
            await InvokeAsync(StateHasChanged);
            // ✅ triggers the seed when the map is ready (or immediately if the map has already booted).
            await NotifyDataLoadedAsync(fit: true);
        }
        protected override async Task OnMapReadyAsync()
        {
            // Optional: a "safe" fit
            try { await MapInterop.RefreshSizeAsync(ScopeKey); } catch { }

            // If hub updates arrived before, you can replay them after seeding.
            while (_pendingHubUpdates.TryDequeue(out var dto))
                await UpsertCalendarMarkerAsync(dto);

            try { await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey); } catch { }
            await Task.Delay(50);
            try { await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey); } catch { }
        }

        protected override async Task SeedAsync(bool fit)
        {
            // 1) clear calendar markers
            //await MapInterop.ClearCrowdCalendarMarkersAsync(ScopeKey);

            // 2) Source: You can use _all or Filter(_visible)
            var src = _all; // or Filter(_visible).ToList()

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", src, ScopeKey);

            if (fit && src.Count > 0)
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await JS.InvokeVoidAsync("OutZenInterop.fitToCalendar", ScopeKey, new { maxZoom = 17 });
            }
            Console.WriteLine($"[CIC] SeedAsync: booted={IsMapBooted} count={_all?.Count}");

            var st = await JS.InvokeAsync<object>("OutZenInterop.dumpState", ScopeKey);
            Console.WriteLine($"[CIC] dumpState: {System.Text.Json.JsonSerializer.Serialize(st)}");
        }

        private async Task FitThrottledAsync(int ms = 250)
        {
            var now = Environment.TickCount64;
            if (now - _lastFitTicks < ms) return;
            _lastFitTicks = now;

            try
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await JS.InvokeVoidAsync("OutZenInterop.fitToCalendar", ScopeKey, new { maxZoom = 17 });
            }
            catch { }
        }

        private async Task UpsertCalendarMarkerAsync(ClientCrowdInfoCalendarDTO dto)
        {
            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) return;
            if (dto.Latitude == 0 && dto.Longitude == 0) return;

            var lvl = Math.Clamp(dto.ExpectedLevel.GetValueOrDefault(), 1, SharedConstants.MaxCrowdLevel);

            static string FmtTs(TimeSpan? ts) => ts is null ? "—" : ts.Value.ToString(@"hh\:mm\:ss");

            await MapInterop.UpsertCrowdCalendarMarkerAsync(
                id: CICMarkerId(dto.Id),              // <= "cc:{id}"
                lat: dto.Latitude,
                lng: dto.Longitude,
                level: lvl,
                info: new
                {
                    eventname = dto.EventName,
                    description =
                        $"Start {FmtTs(dto.StartLocalTime)} • End {FmtTs(dto.EndLocalTime)} • " +
                        $"LeadHours {dto.LeadHours} • Confidence {dto.Confidence}%",
                    messagetemplate = dto.MessageTemplate,
                    active = dto.Active,
                    icon = "🥁🎉"
                },
                scopeKey: ScopeKey
            );
            Console.WriteLine($"[CIC] upsert {CICMarkerId(dto.Id)} ll={dto.Latitude},{dto.Longitude} lvl={lvl}");
        }

        // ----------------------------
        // Loading + pagination
        // ----------------------------
        private async Task LoadAllAsync()
        {
            var fetched = (await CrowdInfoCalendarService.GetAllSafeAsync())?.ToList() ?? new List<ClientCrowdInfoCalendarDTO>();

            CrowdInfoCalendars = fetched;
            _all = fetched;

            _visible.Clear();
            _currentIndex = 0;
            LoadMoreItems();

            _lastLevels.Clear();
            foreach (var co in fetched)
                _lastLevels[co.Id] = co.ExpectedLevel.GetValueOrDefault();
        }

        private void LoadMoreItems()
        {
            var next = _all.Skip(_currentIndex).Take(PageSize).ToList();
            _visible.AddRange(next);
            _currentIndex += next.Count;
        }

        private IEnumerable<ClientCrowdInfoCalendarDTO> Filter(IEnumerable<ClientCrowdInfoCalendarDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x =>
                    string.IsNullOrEmpty(q) ||
                    (x.EventName ?? "").Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Latitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    x.Longitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.DateUtc >= cutoff);
        }

        // ----------------------------
        // SignalR
        // ----------------------------
        private async Task StartSignalRAsync()
        {
            // If you have IHubUrlBuilder, prefer:
            // var hubUrl = HubUrls.Build(HubPaths.CrowdCalendar); // according to your project
            // Alternatively, manual build:
            var hubUrl = HubUrls.Build(CrowdCalendarHubMethods.HubPath);

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            _hub.On<ClientCrowdInfoCalendarDTO>("ReceiveCrowdCalendarUpdate", async dto =>
            {
                UpsertLocal(dto);

                if (!IsMapBooted)
                {
                    _pendingHubUpdates.Enqueue(dto);
                    await InvokeAsync(StateHasChanged);
                    return;
                }

                await UpsertCalendarMarkerAsync(dto);
                await InvokeAsync(StateHasChanged);
            });

            _hub.On<int>("CrowdInfoArchived", async id =>
            {
                CrowdInfoCalendars.RemoveAll(c => c.Id == id);
                _all.RemoveAll(c => c.Id == id);
                _visible.RemoveAll(c => c.Id == id);

                if (IsMapBooted)
                {
                    try { await MapInterop.RemoveCrowdCalendarMarkerAsync(CICMarkerId(id), ScopeKey); } catch { }
                }

                await InvokeAsync(StateHasChanged);
            });

            await _hub.StartAsync();
            Console.WriteLine($"✅ Connected to {hubUrl}");

            // catch-up: if the map is already booted when hub starts
            if (IsMapBooted && _visible.Count > 0)
                await SyncMapMarkersAsync(fit: true);
        }

        private void UpsertLocal(ClientCrowdInfoCalendarDTO dto)
        {
            static void Upsert(List<ClientCrowdInfoCalendarDTO> list, ClientCrowdInfoCalendarDTO item)
            {
                var i = list.FindIndex(c => c.Id == item.Id);
                if (i >= 0) list[i] = item; else list.Add(item);
            }

            Upsert(CrowdInfoCalendars, dto);
            Upsert(_all, dto);

            var j = _visible.FindIndex(c => c.Id == dto.Id);
            if (j >= 0) _visible[j] = dto;
        }

        // ----------------------------
        // Map markers
        // ----------------------------
        public async Task ClearCrowdCalendarMarkersAsync(string scopeKey)
        {
            try
            {
                await JS.InvokeVoidAsync("OutZenInterop.clearCrowdCalendarMarkers", scopeKey);
            }
            catch
            {
                // fallback: clear all outzen layers OR do nothing
                // await _js.InvokeVoidAsync("OutZenInterop.clearAllOutZenLayers", scopeKey);
            }
        }
        private async Task SyncMapMarkersAsync(bool fit)
        {
            if (!IsMapBooted) return;

            var items = Filter(_visible).ToList();

            // Clear calendar markers (global wrapper)
            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdCalendarMarkers", ScopeKey); } catch { }

            foreach (var co in items)
                await ApplySingleMarkerUpdateAsync(co, alreadyBooted: true);

            if (fit && items.Any())
            {
                await FitThrottledAsync();
            }
        }

        private async Task ApplySingleMarkerUpdateAsync(ClientCrowdInfoCalendarDTO dto, bool alreadyBooted = false)
        {
            if (!alreadyBooted && !IsMapBooted) return;

            if (!double.IsFinite(dto.Latitude) || !double.IsFinite(dto.Longitude)) return;
            if (dto.Latitude == 0 && dto.Longitude == 0) return;

            var lvl = Math.Clamp(dto.ExpectedLevel.GetValueOrDefault(), 1, SharedConstants.MaxCrowdLevel);
            static string FmtTs(TimeSpan? ts) => ts is null ? "—" : ts.Value.ToString(@"hh\:mm\:ss");

            await JS.InvokeVoidAsync("OutZenInterop.addOrUpdateCrowdCalendarMarker",
                CICMarkerId(dto.Id),
                dto.Latitude,
                dto.Longitude,
                lvl,
                new
                {
                    eventname = dto.EventName,
                    description =
                        $"Start Local Time {FmtTs(dto.StartLocalTime)}, " +
                        $"End Local Time {FmtTs(dto.EndLocalTime)}, " +
                        $"LeadHours {dto.LeadHours}, Confidence {dto.Confidence} %",
                    messagetemplate = dto.MessageTemplate?.ToString(),
                    active = dto.Active,
                    icon = "🥁🎉"
                },
                ScopeKey);
        }

        // ----------------------------
        // Scroll + filters (call from razor)
        // ----------------------------
        private async Task HandleScroll()
        {
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
        // Navigation helpers (optionnels)
        // ----------------------------
        private void GoNew() => Navigation.NavigateTo("/crowdcalendar/new");
        private void GoDetail(int id) => Navigation.NavigateTo($"/calendar/{id}");

        private static string InfoDescCalendar(ClientCrowdInfoCalendarDTO co)
            => CrowdInfoSeverityHelpers.GetDescription(CrowdInfoSeverityHelpers.GetSeverity(co));

        private static string GetLevelCss(int level)
        {
            var safe = Math.Clamp(level, 0, 5);
            return $"info--lvl{safe}";
        }

        private void ClickInfo(int id) => SelectedId = id;

        private async Task Load()
        {
            // ⚠️ Adapt to your actual service (existing methods)
            // Idea: you call a filtered endpoint: from/to/region/placeId/active
            // If you don't have a filtered method, fallback -> LoadAllAsync()

            if (CrowdInfoCalendarService is null)
                return;

            bool? active = activeFilter switch
            {
                "true" => true,
                "false" => false,
                _ => null
            };

            List<ClientCrowdInfoCalendarDTO> fetched;

            // ✅ If you have a filtered method, use it:
            // fetched = (await CrowdInfoCalendarService.SearchAsync(from, to, region, placeId, active))?.ToList() ?? new();

            // ✅ Alternatively: simple fallback (then client-side filtering)
            fetched = (await CrowdInfoCalendarService.GetAllSafeAsync())?.ToList() ?? new();

            // Minimal local filtering (if no SearchAsync)
            fetched = fetched
                .Where(x => x.DateUtc.Date >= from.Date && x.DateUtc.Date <= to.Date)
                .Where(x => string.IsNullOrWhiteSpace(region) || string.Equals(x.RegionCode, region, StringComparison.OrdinalIgnoreCase))
                .Where(x => !placeId.HasValue || x.PlaceId == placeId.Value)
                .Where(x =>
                {
                    if (active is null) return true;
                    return x.Active == active.Value;
                })
                .ToList();

            allCrowdInfoCalendars = fetched;

            // You can also feed your existing pipeline
            CrowdInfoCalendars = fetched;
            _all = fetched;
            _visible.Clear();
            _currentIndex = 0;
            LoadMoreItems();

            if (IsMapBooted) await SyncMapMarkersAsync(fit: true);

            await InvokeAsync(StateHasChanged);
        }

        private async Task LoadAll()
        {
            await LoadAllAsync();
            allCrowdInfoCalendars = _all;

            if (IsMapBooted) await SyncMapMarkersAsync(fit: true);
            await InvokeAsync(StateHasChanged);
        }

        // ----------------------------
        // Dispose
        // ----------------------------
        protected override async Task OnBeforeDisposeAsync()
        {
            _disposed = true;

            if (_hub is not null)
            {
                try { await JS.InvokeVoidAsync("OutZenInterop.unregisterDotNetRef", ScopeKey); } catch { }
                try { await JS.InvokeVoidAsync("OutZenInterop.disposeOutZen", new { mapId = MapId, scopeKey = ScopeKey }); } catch { }
                try { await _hub.StopAsync(); } catch { }
                try { await _hub.DisposeAsync(); } catch { }
            }

            //WeatherHub.HeavyRainReceived -= OnHeavyRainReceived;
        }
    }
}




































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/
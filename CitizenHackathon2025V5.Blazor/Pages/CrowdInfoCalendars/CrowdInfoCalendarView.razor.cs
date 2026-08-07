// CrowdInfoCalendarView.razor.cs
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.Shared;
using CitizenHackathon2025V5.Blazor.Client.Utils;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Collections.Concurrent;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfoCalendars
{
    public partial class CrowdInfoCalendarView
    {
    #nullable disable
        [Inject] public CrowdInfoCalendarService CrowdInfoCalendarService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;
        [Inject] public IHubUrlBuilder HubUrls { get; set; } = default!;

        protected override string ScopeKey => "crowdinfocalendarview";
        protected override string MapId => "leafletMap-crowdinfocalendarview";
        protected override bool ClearAllOnMapReady => false;
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        protected override string AllowedMarkerPrefix => "cc:";

        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;
        protected override bool ForceBootOnFirstRender => false;
        protected override bool ResetMarkersOnBoot => false;

        private HubConnection _hub;

        private DateTime from = DateTime.UtcNow.Date.AddDays(-7);
        private DateTime to = DateTime.UtcNow.Date.AddDays(+7);
        private string region = "";
        private int? placeId = null;
        private long _lastFitTicks;
        private static string CICMarkerId(int id) => $"cc:{id}";
        private string activeFilter = "";
        private string eventName = "";

        private List<ClientCrowdInfoCalendarDTO> allCrowdInfoCalendars;

        public List<ClientCrowdInfoCalendarDTO> CrowdInfoCalendars { get; set; } = new();
        private List<ClientCrowdInfoCalendarDTO> _all = new();

        /*
         * Full source of the table :
         * Filtered and sorted results, without pagination.
         */
        private List<ClientCrowdInfoCalendarDTO> _tableSource = new();

        /*
         * Currently visible rows in the dropdown list.
         */
        private readonly List<ClientCrowdInfoCalendarDTO> _visible = new();

        private int _currentIndex;

        /*
         * Five records loaded at a time.
         */
        private const int PageSize = 5;

        private ElementReference ScrollContainerRef;
        private string _q = string.Empty;

        private bool _onlyRecent;
        private bool _hubStarted;
        private bool _seedCompleted;
        private bool _disposed;

        private readonly ConcurrentQueue<ClientCrowdInfoCalendarDTO> _pendingHubUpdates = new();
        private readonly Dictionary<int, int> _lastLevels = new();

        public int SelectedId { get; set; }

        protected override async Task OnInitializedAsync()
        {
            await LoadAllAsync();
            allCrowdInfoCalendars = _all;
            await InvokeAsync(StateHasChanged);
            await NotifyDataLoadedAsync(fit: true);
        }

        protected override async Task OnMapReadyAsync()
        {
            try { await MapInterop.RefreshSizeAsync(ScopeKey); } catch { }

            if (!_hubStarted)
            {
                _hubStarted = true;
                await StartSignalRAsync();
            }

            while (_pendingHubUpdates.TryDequeue(out var dto))
                await UpsertCalendarMarkerAsync(dto);
        }

        protected override async Task SeedAsync(bool fit)
        {
            var src = BuildTableSource();

            await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", src, ScopeKey);

            if (fit && src.Count > 0)
            {
                await MapInterop.RefreshSizeAsync(ScopeKey);
                await JS.InvokeVoidAsync("OutZenInterop.fitToCalendar", ScopeKey, new { maxZoom = 17 });
            }

            _seedCompleted = true;

            Console.WriteLine($"[CIC] SeedAsync: booted={IsMapBooted} count={src.Count}");

            var st = await JS.InvokeAsync<object>("OutZenInterop.dumpState", ScopeKey);
            Console.WriteLine($"[CIC] dumpState: {System.Text.Json.JsonSerializer.Serialize(st)}");

            try { await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey); } catch { }
            await Task.Delay(50);
            try { await JS.InvokeVoidAsync("OutZenInterop.refreshMapSize", ScopeKey); } catch { }
        }

        private async Task OpenCalendarDetail(int id)
        {
            if (id <= 0)
                return;

            SelectedId = id;

            /*
             * First, render the Detail window.
             */
            await InvokeAsync(StateHasChanged);

            if (!IsMapBooted)
                return;

            try
            {
                await MapInterop.EnsureAsync();

                var markerId = CICMarkerId(id);

                var highlighted = await JS.InvokeAsync<bool>("OutZenInterop.highlightMarkerById",

                        /*
                         * Example: cc:121
                         */
                        markerId,

                        ScopeKey,

                        /*
                         * Calendar markers are not
                         * clustered, but this zoom level
                         * allows a good view of the location.
                         */
                        16,

                        /*
                         * The form already contains the information.                    
                         * the information.
                         */
                        false,

                        /*
                         * Move the marker up so that it
                         * is not hidden by the window.
                         */
                        120
                    );

                Console.WriteLine($"[CIC Highlight] " + $"markerId={markerId}, " + $"highlighted={highlighted}");
            }
            catch (JSException ex)
            {
                Console.Error.WriteLine($"[CIC Highlight] JS failure " + $"for cc:{id}: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CIC Highlight] failure " + $"for cc:{id}: {ex.Message}");
            }
        }

        private async Task CloseCalendarDetail()
        {
            try
            {
                if (IsMapBooted)
                {
                    await MapInterop.EnsureAsync();

                    await JS.InvokeAsync<bool>("OutZenInterop.clearHighlightedMarker", ScopeKey);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CIC Highlight] " + $"clear failed: {ex.Message}");
            }

            SelectedId = 0;

            await InvokeAsync(StateHasChanged);

            try
            {
                if (IsMapBooted)
                {
                    await FitThrottledAsync();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CIC Highlight] " + $"fit after close failed: {ex.Message}");
            }
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
                id: CICMarkerId(dto.Id),
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
        }
        private async Task LoadAllAsync()
        {
            var fetched = (await CrowdInfoCalendarService.GetAllSafeAsync())?.ToList()
                          ?? new List<ClientCrowdInfoCalendarDTO>();

            CrowdInfoCalendars = fetched;
            _all = fetched;

            /*
             * The table includes only the first five
             * sorted rows.
             */
            ResetVisibleRows();

            _lastLevels.Clear();

            foreach (var co in fetched)
                _lastLevels[co.Id] = co.ExpectedLevel.GetValueOrDefault();
        }

        private List<ClientCrowdInfoCalendarDTO> BuildTableSource()
        {
            var today = DateTime.Today;

            /*
             * By default :
             * - past dates are hidden ;
             * - today appears first ;
             * - future dates follow in order ;
             * - events within a day follow their time.
             */
            return Filter(_all)
                .Where(x => x.DateUtc.Date >= today)
                .OrderBy(x => x.DateUtc.Date)
                .ThenBy(x => x.StartLocalTime ?? TimeSpan.Zero)
                .ThenBy(x => x.EventName ?? string.Empty)
                .ThenBy(x => x.RegionCode ?? string.Empty)
                .ThenBy(x => x.Id)
                .ToList();
        }

        private void ResetVisibleRows()
        {
            /*
             * Rebuilds the complete source according to the filters
             * and chronological order.
             */
            _tableSource = BuildTableSource();

            /*
             * Starts from the first page.
             */
            _visible.Clear();
            _currentIndex = 0;

            LoadMoreItems();
        }

        private void LoadMoreItems()
        {
            if (_currentIndex >= _tableSource.Count)
                return;

            var next = _tableSource
                .Skip(_currentIndex)
                .Take(PageSize)
                .ToList();

            _visible.AddRange(next);
            _currentIndex += next.Count;

            Console.WriteLine(
                $"[CIC][Table] Loaded={_visible.Count}, " +
                $"Total={_tableSource.Count}, " +
                $"PageSize={PageSize}");
        }

        private IEnumerable<ClientCrowdInfoCalendarDTO> Filter(IEnumerable<ClientCrowdInfoCalendarDTO> source)
        {
            var q = _q?.Trim();

            var today = DateTime.Today;
            var recentEnd = today.AddDays(7);

            return source
                .Where(x => string.IsNullOrWhiteSpace(q) || (x.EventName ?? string.Empty)
                        .Contains(q, StringComparison.OrdinalIgnoreCase) ||

                    (x.RegionCode ?? string.Empty)
                        .Contains(q, StringComparison.OrdinalIgnoreCase) ||

                    x.Latitude.ToString()
                        .Contains(q, StringComparison.OrdinalIgnoreCase) ||

                    x.Longitude.ToString()
                        .Contains(q, StringComparison.OrdinalIgnoreCase))

                /*
                 * "Recent" now means "recent." :
                 * today and the next seven days.
                 */
                .Where(x => !_onlyRecent || (x.DateUtc.Date >= today && x.DateUtc.Date <= recentEnd));
        }

        private async Task StartSignalRAsync()
        {
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

                ResetVisibleRows();

                if (IsMapBooted)
                {
                    try { await MapInterop.RemoveCrowdCalendarMarkerAsync(CICMarkerId(id), ScopeKey); } catch { }
                }

                await InvokeAsync(StateHasChanged);
            });

            await _hub.StartAsync();

            Console.WriteLine($"✅ Connected to {hubUrl}");

            if (IsMapBooted && _visible.Count > 0)
                await SyncMapMarkersAsync(fit: true);
        }

        private void UpsertLocal(ClientCrowdInfoCalendarDTO dto)
        {
            static void Upsert(List<ClientCrowdInfoCalendarDTO> list, ClientCrowdInfoCalendarDTO item)
            {
                var index = list.FindIndex(c => c.Id == item.Id);

                if (index >= 0)
                {
                    list[index] = item;
                }
                else
                {
                    list.Add(item);
                }
            }

            Upsert(CrowdInfoCalendars, dto);
            Upsert(_all, dto);

            /*
             * Replace the element as needed
             * new DateUtc and reload five lines.
             */
            ResetVisibleRows();
        }

        public async Task ClearCrowdCalendarMarkersAsync(string scopeKey)
        {
            try
            {
                await JS.InvokeVoidAsync("OutZenInterop.clearCrowdCalendarMarkers", scopeKey);
            }
            catch
            {
            }
        }

        private async Task SyncMapMarkersAsync(bool fit)
        {
            if (!IsMapBooted) return;

            var items = BuildTableSource();

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdCalendarMarkers", ScopeKey); } catch { }
            try { await JS.InvokeVoidAsync("OutZenInterop.upsertCrowdCalendarMarkers", items, ScopeKey); } catch { }

            if (fit && items.Any())
                await FitThrottledAsync();
        }


        private async Task HandleScroll()
        {
            try
            {
                var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);

                var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);

                var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

                var reachedBottom = scrollTop + clientHeight >= scrollHeight - 8;

                if (!reachedBottom)
                    return;

                if (_currentIndex >= _tableSource.Count)
                    return;

                /*
                 * Loads five additional lines.
                 *
                 * We do not resynchronize the map:
                 * it already has all the filtered results.
                 */
                LoadMoreItems();

                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CIC][TableScroll] {ex.Message}");
            }
        }

        private async Task ToggleRecent()
        {
            _onlyRecent = !_onlyRecent;

            /*
             * Recalculate the first five lines.
             */
            ResetVisibleRows();

            if (IsMapBooted)
            {
                await SyncMapMarkersAsync(fit: true);
            }

            await InvokeAsync(StateHasChanged);
        }

        private string Q
        {
            get => _q;

            set
            {
                if (_q == value)
                    return;

                _q = value ?? string.Empty;

                /*
                 * The table starts again from five lines according to
                 * the new research.
                 */
                ResetVisibleRows();

                if (IsMapBooted)
                {
                    _ = SyncMapMarkersAsync(fit: true);
                }
            }
        }

        private void GoNew() => Navigation.NavigateTo("/crowdcalendar/new");
        private void GoDetail(int id)
        {
            if (id <= 0)
                return;

            Navigation.NavigateTo($"/crowdcalendar/{id}");
        }

        private static string InfoDescCalendar(ClientCrowdInfoCalendarDTO co)
            => CrowdInfoSeverityHelpers.GetDescription(CrowdInfoSeverityHelpers.GetSeverity(co));

        private static string GetLevelCss(int level)
        {
            var safe = Math.Clamp(level, 0, 5);
            return $"info--lvl{safe}";
        }

        private async Task Load()
        {
            if (CrowdInfoCalendarService is null)
                return;

            bool? active = activeFilter switch
            {
                "true" => true,
                "false" => false,
                _ => null
            };

            List<ClientCrowdInfoCalendarDTO> fetched;

            if (!string.IsNullOrWhiteSpace(eventName))
            {
                fetched = await CrowdInfoCalendarService.GetByEventNameAsync(
                    eventName.Trim(),
                    active ?? true);
            }
            else if (placeId.HasValue && placeId.Value > 0)
            {
                fetched = await CrowdInfoCalendarService.GetByPlaceIdAsync(
                    placeId.Value,
                    active ?? true);
            }
            else if (!string.IsNullOrWhiteSpace(region))
            {
                fetched = await CrowdInfoCalendarService.GetByRegionCodeAsync(
                    region.Trim(),
                    active ?? true);
            }
            else
            {
                fetched = await CrowdInfoCalendarService.GetAllSafeAsync()
                          ?? new List<ClientCrowdInfoCalendarDTO>();
            }

            fetched = fetched
                .Where(x => x.DateUtc.Date >= from.Date && x.DateUtc.Date <= to.Date)
                .Where(x => active is null || x.Active == active.Value)
                .ToList();

            allCrowdInfoCalendars = fetched;
            CrowdInfoCalendars = fetched;
            _all = fetched;

            ResetVisibleRows();

            if (IsMapBooted)
                await SyncMapMarkersAsync(fit: true);

            await InvokeAsync(StateHasChanged);
        }

        private async Task LoadAll()
        {
            await LoadAllAsync();
            allCrowdInfoCalendars = _all;

            if (IsMapBooted) await SyncMapMarkersAsync(fit: true);
            await InvokeAsync(StateHasChanged);
        }

        protected override async Task OnBeforeDisposeAsync()
        {
            try
            {
                await ClearDetailMarkerHighlightAsync(restoreOverview: false);
            }
            catch
            {
            }
            _disposed = true;

            if (_hub is not null)
            {
                try { await JS.InvokeVoidAsync("OutZenInterop.unregisterDotNetRef", ScopeKey); } catch { }
                try { await _hub.StopAsync(); } catch { }
                try { await _hub.DisposeAsync(); } catch { }
            }
        }
    }
}




































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/
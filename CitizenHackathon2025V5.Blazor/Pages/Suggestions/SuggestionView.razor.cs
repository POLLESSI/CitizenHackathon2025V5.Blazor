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
using Newtonsoft.Json;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionView : OutZenMapPageBase
    {
    #nullable disable
        [Inject]
        public HttpClient Client { get; set; } 
        [Inject] public SuggestionService SuggestionService { get; set; }
        [Inject] public SuggestionMapService SuggestionMapService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        [JSInvokable]
        public Task SelectSuggestionFromMap(int id)
        {
            SelectedId = id;
            StateHasChanged();
            return Task.CompletedTask;
        }
        //private const string ApiBase = "https://localhost:7254";

        // ===== State =====
        private bool _disposed;
        private bool _mapBooted;
        private bool _markersSeeded;
        private bool _bootRequested;
        private bool _testMarkerAdded;
        public List<ClientSuggestionDTO> Suggestions { get; set; }
        private List<ClientSuggestionDTO> allSuggestions = new();
        private readonly List<ClientSuggestionDTO> visibleSuggestions = new();
        private List<SuggestionGroupedByPlaceDTO> _grouped = new();
        private DotNetObjectReference<SuggestionView> _dotnetRef;
        private int currentIndex = 0;
        private const int PageSize = 100;
        private long _lastFitTicks;
        protected override string ScopeKey => "suggestionview";
        protected override string MapId => "leafletMap-suggestionview";
        protected override (double lat, double lng) DefaultCenter => (50.89, 4.34);
        protected override int DefaultZoom => 14;

        // ===== Hub =====
        private HubConnection hubConnection;

        // Optional
        protected override bool ForceBootOnFirstRender => true;
        protected override bool ResetMarkersOnBoot => true;
        private static string SuMarkerId(int id) => $"suggestion:{id}";

        private string _token;
        // ===== UI state used by .razor =====
        private ElementReference ScrollContainerRef;
        private string _q = string.Empty;
        private bool _onlyRecent;
        public int SelectedId { get; set; }   // must be accessible from the .razor
        protected override async Task OnInitializedAsync()
        {
            allSuggestions = await SuggestionService.GetLatestSuggestionAsync();
            Suggestions = allSuggestions; // IMPORTANT: Avoid “2 different sources”

            currentIndex = 0;
            visibleSuggestions.Clear();
            LoadMoreItems();

            _grouped = allSuggestions
                .Where(s => s.Latitude.HasValue && s.Longitude.HasValue)
                .GroupBy(s => s.OriginalPlace ?? "(unknown)")
                .Select(g => new SuggestionGroupedByPlaceDTO
                {
                    PlaceName = g.Key,
                    Latitude = g.First().Latitude!.Value,
                    Longitude = g.First().Longitude!.Value,
                    SuggestionCount = g.Count(),
                    LastSuggestedAt = g.Max(x => x.DateSuggestion),
                    Suggestions = g.ToList()
                })
                .ToList();

            Console.WriteLine($"[SuggestionView] Loaded allSuggestions={allSuggestions.Count}");
        }

        protected override async Task OnMapReadyAsync()
        {
            // At this stage: map booted + container OK
            await FitThrottledAsync();
            await Task.Delay(50);
            await FitThrottledAsync();

            await ReseedSuggestionMarkersAsync(fit: true);
        }

        private async Task ReseedSuggestionMarkersAsync(bool fit)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;

            try { await JS.InvokeVoidAsync("OutZenInterop.clearCrowdMarkers", ScopeKey); } catch { }

            foreach (var dto in allSuggestions)
                await ApplySingleSuggestionMarkerAsync(dto);

            await FitThrottledAsync();
            if (fit)
            {
                await FitThrottledAsync();
            }
        }

        private async Task ApplySingleSuggestionMarkerAsync(ClientSuggestionDTO dto)
        {
            if (_disposed) return;
            if (!IsMapBooted) return;
            if (dto is null) return;

            // Normalize nullable -> non-nullable
            double lat = dto.Latitude.GetValueOrDefault(double.NaN);
            double lng = dto.Longitude.GetValueOrDefault(double.NaN);

            // Validate
            if (!double.IsFinite(lat) || !double.IsFinite(lng) ||
                lat < -90 || lat > 90 || lng < -180 || lng > 180 ||
                (lat == 0 && lng == 0))
            {
                lat = 50.85;
                lng = 4.35;
            }

            await JS.InvokeVoidAsync(
                "OutZenInterop.addOrUpdateCrowdMarker",
                SuMarkerId(dto.Id),
                lat,
                lng,
                2, // <- default level if you want (or MapSuggestionCountToLevel(...))
                new
                {
                    title = dto.Title ?? "Suggestion",
                    suggestionAlternative = dto.SuggestedAlternatives,
                    reason = dto.Reason,
                    distanceKm = dto.DistanceKm,
                    context = dto.Context,
                    icon = "💡"
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
        private void LoadMoreItems()
        {
            var next = allSuggestions.Skip(currentIndex).Take(PageSize).ToList();
            visibleSuggestions.AddRange(next);
            currentIndex += next.Count;
        }
        private async Task SeedSuggestionBundlesAsync(bool fit)
        {
            if (!_mapBooted) return;

            var places = _grouped.Select((g, idx) => new
            {
                Id = idx + 1,
                Name = g.PlaceName,
                Latitude = (double)g.Latitude,
                Longitude = (double)g.Longitude
            }).ToList();

            var suggestions = _grouped.Select((g, idx) => new
            {
                Id = idx + 1,
                PlaceId = idx + 1,
                Summary = $"{g.SuggestionCount} suggestion(s)",
                Description = $"Last : {g.LastSuggestedAt:yyyy-MM-dd HH:mm}"
            }).ToList();

            var payload = BuildSuggestionDetailsPayload();

            var withCoords = allSuggestions.Where(x => x.Latitude.HasValue && x.Longitude.HasValue).ToList();
            foreach (var x in withCoords.Take(10))
            {
                Console.WriteLine($"[SUGG COORD] id={x.Id} title={x.Title} lat={x.Latitude} lng={x.Longitude} orig={x.OriginalPlace}");
            }

            await JS.InvokeAsync<bool>("OutZenInterop.addOrUpdateDetailMarkers", payload, ScopeKey);
            await FitThrottledAsync();
            Console.WriteLine($"[SuggestionView] allSuggestions={allSuggestions?.Count ?? 0} coords={allSuggestions?.Count(x => x.Latitude.HasValue && x.Longitude.HasValue) ?? 0}");

            await FitThrottledAsync();
        }

        private static int MapSuggestionCountToLevel(int count)
        {
            if (count <= 0) return 1;
            if (count <= 2) return 2;
            if (count <= 5) return 3;
            return 4; // many requests -> red
        }


        private void ClickInfo(int id) => SelectedId = id;

        // Infinite scrolling (uses JS helpers: getScrollTop/getScrollHeight/getClientHeight)
        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (currentIndex < allSuggestions.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private object BuildSuggestionDetailsPayload()
        {
            var source = allSuggestions ?? new List<ClientSuggestionDTO>();

            var sugg = source
                .Where(x => x.Latitude.HasValue && x.Longitude.HasValue)
                .Select(x => new
                {
                    SuggestionId = x.Id,
                    x.Id,
                    x.Title,
                    x.Reason,
                    x.OriginalPlace,
                    x.SuggestedAlternatives,
                    x.DistanceKm,
                    Latitude = x.Latitude!.Value,
                    Longitude = x.Longitude!.Value
                })
                .ToList();

            return new
            {
                places = Array.Empty<object>(),
                events = Array.Empty<object>(),
                crowds = Array.Empty<object>(),
                traffic = Array.Empty<object>(),
                weather = Array.Empty<object>(),
                gpt = Array.Empty<object>(),
                suggestions = sugg
            };
        }
        private IEnumerable<ClientSuggestionDTO> FilterSuggestion(IEnumerable<ClientSuggestionDTO> source)
        {
            if (source is null) return Array.Empty<ClientSuggestionDTO>();
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (!string.IsNullOrEmpty(x.OriginalPlace) && x.OriginalPlace.Contains(q, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(x.SuggestedAlternatives) && x.SuggestedAlternatives.Contains(q, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(x.Reason) && x.Reason.Contains(q, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(x.Context) && x.Context.Contains(q, StringComparison.OrdinalIgnoreCase))
                );
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

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





using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using CitizenHackathon2025V5.Blazor.Client.DTOs.JsInterop;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Globalization;
using System.Reflection;
//SuggestionView: scopeKey = "suggestions" (or event:{ id} if contextualized)

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionView : IAsyncDisposable
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

        private const string ApiBase = "https://localhost:7254";
        private bool _mapBooted;
        private bool _markersSeeded;
        private bool _bootRequested;
        private bool _testMarkerAdded;
        public List<ClientSuggestionDTO> Suggestions { get; set; }
        private List<ClientSuggestionDTO> allSuggestions = new();
        private List<ClientSuggestionDTO> visibleSuggestions = new();
        private List<SuggestionGroupedByPlaceDTO> _grouped = new();
        private int currentIndex = 0;
        private const int PageSize = 100;
        private readonly string _instanceId = Guid.NewGuid().ToString("N");
        private readonly HashSet<string> _bundleSeededScopes = new();
        private string _bootToken;
        private string ScopeKey => $"suggestions:{_instanceId}";
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";
        private string _mapId => $"leafletMap-suggestions-{_instanceId}";

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;
        private bool IsSeeded(string scopeKey) => _bundleSeededScopes.Contains(scopeKey);
        private void MarkSeeded(string scopeKey) => _bundleSeededScopes.Add(scopeKey);
        private void ResetSeeded(string scopeKey) => _bundleSeededScopes.Remove(scopeKey);

        protected override async Task OnInitializedAsync()
        {
            // 1) Initial REST
            var fetched = (await SuggestionService.GetLatestSuggestionAsync())?.ToList()
                          ?? new List<ClientSuggestionDTO>();

            Suggestions = fetched;
            allSuggestions = fetched;
            visibleSuggestions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            _grouped = await SuggestionMapService.GetSuggestionMapAsync(days: 7);

            // SuggestionHubMethods.HubPath = "/hubs/suggestionHub"
            var url = HubUrls.Build(SuggestionHubMethods.HubPath);
            // => https://localhost:7254/hubs/suggestionHub

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () =>
                        await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<ClientSuggestionDTO>(
                SuggestionHubMethods.ToClient.ReceiveSuggestion,
                async dto =>
                {
                    void Upsert(List<ClientSuggestionDTO> list)
                    {
                        var i = list.FindIndex(c => c.Id == dto.Id);
                        if (i >= 0) list[i] = dto;
                        else list.Add(dto);
                    }

                    Upsert(Suggestions);
                    Upsert(allSuggestions);

                    var j = visibleSuggestions.FindIndex(c => c.Id == dto.Id);
                    if (j >= 0) visibleSuggestions[j] = dto;

                    if (_mapBooted)
                    {
                        _grouped = await SuggestionMapService.GetSuggestionMapAsync(days: 7);
                        await SeedSuggestionBundlesAsync(fit: false);
                    }

                    await InvokeAsync(StateHasChanged);
                });

            hubConnection.On(
                SuggestionHubMethods.ToClient.NewSuggestion,
                () => InvokeAsync(StateHasChanged));

            try
            {
                await hubConnection.StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[SuggestionView] Hub start failed: {ex.Message}");
            }
        }
        private void LoadMoreItems()
        {
            var next = allSuggestions.Skip(currentIndex).Take(PageSize).ToList();
            visibleSuggestions.AddRange(next);
            currentIndex += next.Count;
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && !_bootRequested)
            {
                _bootRequested = true;

                await JS.InvokeAsync<bool>("OutZen.ensure");

                var boot = await JS.InvokeAsync<BootResult>("OutZenInterop.bootOutZen", new
                {
                    mapId = _mapId,
                    scopeKey = ScopeKey,
                    center = new[] { 50.89, 4.34 },
                    zoom = 8,
                    enableChart = false,
                    force = false,
                    resetMarkers = true,
                    enableHybrid = false,
                    hybridThreshold = 13
                });

                Console.WriteLine("[SuggestionView] boot done, pushing markers...");
                Console.WriteLine($"[SuggestionView] RENDER scope={ScopeKey} mapId={_mapId} booted={_mapBooted}");


                if (boot is null || !boot.Ok) return;

                _bootToken = boot.Token;
                _mapBooted = true;

                Console.WriteLine($"[Boot] ok={boot?.Ok} mapId={boot?.MapId} scope={boot?.ScopeKey} token={boot?.Token}");
                Console.WriteLine($"[SuggestionView] RENDER scope={ScopeKey} mapId={_mapId} booted={_mapBooted}");

                await Task.Delay(1);
                await JS.InvokeAsync<bool>("OutZenInterop.refreshMapSize", ScopeKey);
                await Task.Delay(50);
                await JS.InvokeAsync<bool>("OutZenInterop.refreshMapSize", ScopeKey);
            }

            if (_mapBooted && !_markersSeeded && _grouped.Count > 0)
            {
                _markersSeeded = true;
                await SeedSuggestionBundlesAsync(fit: true);
            }

            var debug = Navigation.Uri.Contains("debug=1", StringComparison.OrdinalIgnoreCase);

            if (_mapBooted && !_testMarkerAdded && debug)
            {
                _testMarkerAdded = true;
                await JS.InvokeAsync<bool>("OutZenInterop.addOrUpdateCrowdMarker",
                    "test:1", 50.85, 4.35, 4,
                    new { title = "TEST", description = "Marker direct" },
                    ScopeKey);
            }
            Console.WriteLine($"[SuggestionView] RENDER scope={ScopeKey} mapId={_mapId} booted={_mapBooted}");

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

            var payload = new
            {
                places,
                suggestions,
                events = Array.Empty<object>(),
                crowds = Array.Empty<object>(),
                traffic = Array.Empty<object>(),
                weather = Array.Empty<object>(),
                gpt = Array.Empty<object>()
            };

            var ok = await JS.InvokeAsync<bool>(
                "OutZenInterop.addOrUpdateBundleMarkers",
                payload, 80, ScopeKey);

            Console.WriteLine($"[SuggestionView] addOrUpdateBundleMarkers ok={ok}");

            // Debug
            await JS.InvokeVoidAsync("OutZenInterop.debugDumpMarkers", ScopeKey);
            await JS.InvokeVoidAsync("OutZenInterop.debugClusterCount", ScopeKey);

            if (fit)
                await JS.InvokeAsync<bool>("OutZenInterop.fitToBundles", 30, ScopeKey);

            await JS.InvokeAsync<bool>("OutZenInterop.refreshMapSize", ScopeKey);

            await JS.InvokeAsync<bool>("OutZenInterop.addOrUpdateSuggestionMarkers", Suggestions, ScopeKey);
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

        public async ValueTask DisposeAsync()
        {
            var mapId = _mapId; // local copy
            try
            {
                await JS.InvokeAsync<bool>("OutZenInterop.disposeOutZen", new { mapId, scopeKey = ScopeKey, token = _bootToken });
                Console.WriteLine($"[SuggestionView] dispose mapId={_mapId} scope={ScopeKey} token={_bootToken}");
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





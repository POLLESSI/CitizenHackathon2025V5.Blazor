using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using Newtonsoft.Json;
using System.Globalization;
using System.Reflection;

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
        private IJSObjectReference _outZen;
        private bool _mapBooted;
        private bool _markersSeeded;
        public List<ClientSuggestionDTO> Suggestions { get; set; }
        private List<ClientSuggestionDTO> allSuggestions = new();
        private List<ClientSuggestionDTO> visibleSuggestions = new();
        private List<SuggestionGroupedByPlaceDTO> _grouped = new();
        private int currentIndex = 0;
        private const int PageSize = 100;
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

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

                    if (_mapBooted && _outZen is not null)
                    {
                        _grouped = await SuggestionMapService.GetSuggestionMapAsync(days: 7);
                        await SeedSuggestionMarkersAsync(fit: false);
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

        //private static string BuildHubUrl(string baseUrl, string path)
        //{
        //    var b = baseUrl.TrimEnd('/');
        //    var p = path.TrimStart('/');
        //    if (b.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase) &&
        //        p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
        //    {
        //        p = p.Substring("hubs/".Length);
        //    }
        //    return $"{b}/{p}";
        //}
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            // 1) Boot the card only once
            if (firstRender)
            {
                _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

                var ok = await _outZen.InvokeAsync<bool>("bootOutZen", new
                {
                    mapId = "leafletMap",
                    center = new double[] { 50.89, 4.34 },
                    zoom = 8,
                    enableChart = false,
                    force = true
                });

                if (!ok)
                {
                    Console.WriteLine("[SuggestionView] bootOutZen failed.");
                    return;
                }

                _mapBooted = true;
            }

            // 2) AT EACH Render: if the map is ready, if we have data
            //    and that we haven't seeded yet → we seed.
            if (_mapBooted && !_markersSeeded && _grouped.Count > 0)
            {
                _markersSeeded = true;
                await SeedSuggestionMarkersAsync(fit: true);
            }
        }


        private async Task SeedSuggestionMarkersAsync(bool fit)
        {
            if (_outZen is null || !_mapBooted) return;

            // cleans existing markers (crowd/gpt)
            try { await _outZen.InvokeVoidAsync("clearCrowdMarkers"); } catch { }

            Console.WriteLine($"[SuggestionView] _grouped.Count = {_grouped.Count}");

            foreach (var g in _grouped)
            {
                var lat = g.Latitude;  
                var lon = g.Longitude;

                var level = MapSuggestionCountToLevel(g.SuggestionCount);
                var desc = $"{g.SuggestionCount} suggestion(s) – Dernière : {g.LastSuggestedAt:yyyy-MM-dd HH:mm}";

                await _outZen.InvokeVoidAsync("addOrUpdateCrowdMarker",
                    $"sgp:{g.PlaceName}",   // ou un id stable (placeId / placeName hash)
                    (double)g.Latitude,
                    (double)g.Longitude,
                    level,
                    new { title = g.PlaceName, description = desc, icon = "✨" });

            }

            // Debug : dump markers on the JS side
            try { await _outZen.InvokeVoidAsync("debugDumpMarkers"); } catch { }

            try { await _outZen.InvokeVoidAsync("refreshMapSize"); } catch { }
            if (fit)
            {
                await Task.Delay(80);
                try { await _outZen.InvokeVoidAsync("fitToMarkers"); } catch { }
            }
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
            try
            {
                if (_outZen is not null)
                {
                    await _outZen.DisposeAsync();
                }
            }
            catch { /* ignore */ }

            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}


























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





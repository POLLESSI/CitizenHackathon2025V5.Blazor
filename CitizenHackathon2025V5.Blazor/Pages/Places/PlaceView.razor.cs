using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceView : IAsyncDisposable
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  
        [Inject] public PlaceService PlaceService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;

        public List<ClientPlaceDTO> Places { get; set; }
        private List<ClientPlaceDTO> allPlaces = new();
        private List<ClientPlaceDTO> visiblePlaces = new();
        private int currentIndex = 0;
        private const int PageSize = 100;
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await PlaceService.GetLatestPlaceAsync())
                  .Where(p => p != null)
                  .Select(p => p!)    
                  .ToList();
            Places = fetched;
            allPlaces = fetched;
            visiblePlaces.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase.TrimEnd('/');
            var hubPath = "/hubs/placeHub";
            var hubUrl = BuildHubUrl(apiBaseUrl, hubPath);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        var token = await Auth.GetAccessTokenAsync();
                        return token ?? string.Empty;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            // Handlers
            hubConnection.On<ClientPlaceDTO>("ReceivePlaceUpdate", async dto =>
            {
                void Upsert(List<ClientPlaceDTO> list)
                {
                    var i = list.FindIndex(c => c.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(Places);
                Upsert(allPlaces);

                var j = visiblePlaces.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visiblePlaces[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateEventMarker",
                    dto.Id.ToString(), dto.Type, dto.Capacity, dto.Name,
                    new { title = dto.Name, description = $"Maj {dto.Tag}" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("EventArchived", async id =>
            {
                Places.RemoveAll(c => c.Id == id);
                allPlaces.RemoveAll(c => c.Id == id);
                visiblePlaces.RemoveAll(c => c.Id == id);

                await JS.InvokeVoidAsync("window.OutZenInterop.removeMarker", id.ToString());
                await InvokeAsync(StateHasChanged);
            });

            try { await hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[PlaceView] Hub start failed: {ex.Message}"); }
        }
        private void LoadMoreItems()
        {
            var next = allPlaces.Skip(currentIndex).Take(PageSize).ToList();
            visiblePlaces.AddRange(next);
            currentIndex += next.Count;
        }

        private static string BuildHubUrl(string baseUrl, string path)
        {
            var b = baseUrl.TrimEnd('/');
            var p = path.TrimStart('/');
            if (b.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase) &&
                p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("hubs/".Length);
            }
            return $"{b}/{p}";
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;

            _outZen = await JS.InvokeAsync<IJSObjectReference>("import", "/js/app/leafletOutZen.module.js");

            await _outZen.InvokeVoidAsync("bootOutZen", new
            {
                mapId = "leafletMap",
                center = new double[] { 50.89, 4.34 },
                zoom = 13,
                enableChart = true
            });

            await _outZen.InvokeVoidAsync("initCrowdChart", "crowdChart");
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
                if (currentIndex < allPlaces.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private IEnumerable<ClientPlaceDTO> FilterPlace(IEnumerable<ClientPlaceDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                            || x.Latitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent);
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

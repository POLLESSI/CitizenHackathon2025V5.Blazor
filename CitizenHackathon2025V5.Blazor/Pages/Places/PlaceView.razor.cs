using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025.Shared.StaticConfig.Constants;
using CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceView : ComponentBase, IAsyncDisposable
    {
#nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public PlaceService PlaceService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;

        // Data
        public HubConnection hubConnection { get; set; }
        public List<ClientPlaceDTO> Places { get; set; } = new();
        private List<ClientPlaceDTO> allPlaces = new();
        private List<ClientPlaceDTO> visiblePlaces = new();
        private int currentIndex = 0;
        private const int PageSize = 20;

        // UI state
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent; // placeholder (if you want to filter by date later)
        private string _canvasId = $"rotatingEarth-{Guid.NewGuid():N}";
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        private int SelectedId;


        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await PlaceService.GetLatestPlaceAsync()).ToList();
            Places = fetched;
            allPlaces = fetched;
            visiblePlaces.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";

            hubConnection = new HubConnectionBuilder()
                .WithUrl(apiBaseUrl.TrimEnd('/') + PlaceHubMethods.HubPath, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();
            try
            {
                allPlaces = fetched;
                visiblePlaces.Clear();
                currentIndex = 0;
                LoadMoreItems();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlaceView] Init failed: {ex.Message}");
                allPlaces = new();
                visiblePlaces = new();
            }
        }
        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (!firstRender) return;
            await JS.InvokeVoidAsync("initEarth", new
            {
                canvasId = _canvasId,
                speedControlId = _speedId,
                dayUrl = "/images/earth_texture.jpg?v=1",
                nightUrl = "/images/earth_texture_night.jpg?v=1"
            });
        }
        private void LoadMoreItems()
        {
            var next = allPlaces.Skip(currentIndex).Take(PageSize).ToList();
            visiblePlaces.AddRange(next);
            currentIndex += next.Count;
        }
        private async Task HandleScroll()
        {
            try
            {
                await InvokeAsync(StateHasChanged);

                if (currentIndex >= allPlaces.Count) return;

                LoadMoreItems();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[PlaceView] HandleScroll error: {ex.Message}");
            }
        }

        private IEnumerable<ClientPlaceDTO> FilterPlace(IEnumerable<ClientPlaceDTO> source)
        {
            var q = _q?.Trim();

            return source.Where(x =>
                string.IsNullOrEmpty(q)
                || (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (x.Type ?? "").Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        private void Select(int id)
        {
            if (id <= 0)
            {
                Console.WriteLine("[PlaceView] Ignoring click: id <= 0 payload without ID ?)");
                return;
            }
            SelectedId = id;
            Console.WriteLine($"[PlaceView] SelectedId = {SelectedId}");
        }

        private void CloseDetail() => SelectedId = 0;

        public async ValueTask DisposeAsync()
        {
            await Task.CompletedTask;
            try { await JS.InvokeVoidAsync("disposeEarth", _canvasId); } catch { }
        }
    }
}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceView : ComponentBase, IAsyncDisposable
    {
#nullable disable
        [Inject] public PlaceService PlaceService { get; set; }
        [Inject] public IJSRuntime JS { get; set; }

        // Data
        private List<ClientPlaceDTO> allPlaces = new();
        private List<ClientPlaceDTO> visiblePlaces = new();
        private int currentIndex = 0;
        private const int PageSize = 20;

        // UI state
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent; // placeholder (si tu veux filtrer par date plus tard)
        private int SelectedId;

        protected override async Task OnInitializedAsync()
        {
            //hubConnection = new HubConnectionBuilder()
            //    .WithUrl(apiBaseUrl.TrimEnd('/') + PlaceHubMethods.HubPath, options =>
            //    {
            //        options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
            //    })
            //    .WithAutomaticReconnect()
            //    .Build();
            try
            {
                var fetched = (await PlaceService.GetLatestPlaceAsync())
                              .Where(p => p is not null)
                              .Select(p => p!) 
                              .ToList();

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
            //hubConnection.On<string>(PlaceHubMethods.ToClient.NewPlace, payload =>
            //{
            //    Console.WriteLine($"PlaceHub NewPlace: {payload}");
            //    InvokeAsync(StateHasChanged);
            //});

            //// Client -> Serveur
            //await hubConnection.InvokeAsync(PlaceHubMethods.FromClient.RefreshPlace, "refresh places");
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
        }
    }
}
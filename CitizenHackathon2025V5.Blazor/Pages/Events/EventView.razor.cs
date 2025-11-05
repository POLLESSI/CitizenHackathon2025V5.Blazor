using CitizenHackathon2025.Shared.StaticConfig.Constants;
using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventView : IAsyncDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public EventService EventService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;

        public List<ClientEventDTO> Events { get; set; } = new();
        private List<ClientEventDTO> allEvents = new();      
        private List<ClientEventDTO> visibleEvents = new();  
        private int currentIndex = 0;
        private const int PageSize = 20;
        private string _canvasId = $"rotatingEarth-{Guid.NewGuid():N}";
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await EventService.GetLatestEventAsync()).ToList();
            Events = fetched;
            allEvents = fetched;
            visibleEvents.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";

            hubConnection = new HubConnectionBuilder()
                 .WithUrl($"{apiBaseUrl}{EventHubMethods.HubPath}", options =>
                 {
                     // If your hub is later protected, provide a token
                     options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                 })
                 .WithAutomaticReconnect()
                 .Build();

            // Handlers
            hubConnection.On<ClientEventDTO>("ReceiveEventUpdate", async dto =>
            {
                void Upsert(List<ClientEventDTO> list)
                {
                    var i = list.FindIndex(c => c.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(Events);
                Upsert(allEvents);

                var j = visibleEvents.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleEvents[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateEventMarker",
                    dto.Id.ToString(), dto.Latitude, dto.Longitude, dto.Name,
                    new { title = dto.Name, description = $"Maj {dto.DateEvent:HH:mm:ss}" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("EventArchived", async id =>
            {
                Events.RemoveAll(c => c.Id == id);
                allEvents.RemoveAll(c => c.Id == id);
                visibleEvents.RemoveAll(c => c.Id == id);

                await JS.InvokeVoidAsync("window.OutZenInterop.removeMarker", id.ToString());
                await InvokeAsync(StateHasChanged);
            });
           
            try { await hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[EventView] Hub start failed: {ex.Message}"); }
        }

        private void LoadMoreItems()
        {
            var next = allEvents.Skip(currentIndex).Take(PageSize).ToList();
            visibleEvents.AddRange(next);
            currentIndex += next.Count;
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
            await JS.InvokeVoidAsync("initEarth", new
            {
                canvasId = _canvasId,
                speedControlId = _speedId,
                dayUrl = "/images/earth_texture.jpg?v=1",
                nightUrl = "/images/earth_texture_night.jpg?v=1"
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
                if (currentIndex < allEvents.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }

        private IEnumerable<ClientEventDTO> FilterEvent(IEnumerable<ClientEventDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (x.Name ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                            || x.Latitude.ToString().Contains(q, StringComparison.OrdinalIgnoreCase))
                .Where(x => !_onlyRecent || x.DateEvent >= cutoff);
        }

        private void GoToDetail(int id) => Navigation.NavigateTo($"/event/{id}");

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
            try { await JS.InvokeVoidAsync("disposeEarth", _canvasId); } catch { }
        }
    }
}






















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





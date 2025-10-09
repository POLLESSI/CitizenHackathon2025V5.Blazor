using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using CitizenHackathon2025.Shared.StaticConfig.Constants;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionView : IAsyncDisposable
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; } 
        [Inject] public TrafficConditionService TrafficConditionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;

        public List<ClientTrafficConditionDTO> TrafficConditions { get; set; } = new();
        private List<ClientTrafficConditionDTO> allTrafficConditions = new();
        private List<ClientTrafficConditionDTO> visibleTrafficConditions = new();
        private int currentIndex = 0;
        private const int PageSize = 20;
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        // Fields used by .razor
        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await TrafficConditionService.GetLatestTrafficConditionAsync())?.ToList() ?? new();
            TrafficConditions = fetched;
            allTrafficConditions = fetched;
            visibleTrafficConditions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{apiBaseUrl}{TrafficConditionHubMethods.HubPath}", options =>
                {
                    // If your hub is later protected, provide a token
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // Handlers
            hubConnection.On<ClientTrafficConditionDTO>("RefreshTraffic", async dto =>
            {
                if (dto is null) return;
                void Upsert(List<ClientTrafficConditionDTO> list)
                {
                    var i = list.FindIndex(g => g.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(TrafficConditions);
                Upsert(allTrafficConditions);

                var j = visibleTrafficConditions.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleTrafficConditions[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateTrafficConditionMarker",
                    dto.Id.ToString(), dto.IncidentType ?? "", dto.Message ?? "", dto.CongestionLevel ?? "", dto.DateCondition,
                    new { title = dto.IncidentType ?? "", Message = $"Maj {dto.DateCondition:HH:mm:ss}" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("TrafficConditionArchived", async id =>
            {
                TrafficConditions.RemoveAll(c => c.Id == id);
                allTrafficConditions.RemoveAll(c => c.Id == id);
                visibleTrafficConditions.RemoveAll(c => c.Id == id);

                await JS.InvokeVoidAsync("window.OutZenInterop.removeMarker", id.ToString());
                await InvokeAsync(StateHasChanged);
            });
            //hubConnection.On(TrafficConditionHubMethods.ToClient.NotifyNewTraffic, () =>
            //{
            //    Console.WriteLine("notifynewtraffic");
            //    // TODO: reload traffic data or trigger a /api/TrafficCondition/latest request
            //    InvokeAsync(StateHasChanged);
            //});


            try 
            { 
                await hubConnection.StartAsync();
                // Client -> Serveur (no arg)
                //await hubConnection.InvokeAsync(TrafficConditionHubMethods.FromClient.RefreshTraffic);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[TrafficConditionView] Hub start failed: {ex.Message}"); }
        }
        private void LoadMoreItems()
        {
            var next = allTrafficConditions.Skip(currentIndex).Take(PageSize).ToList();
            visibleTrafficConditions.AddRange(next);
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

            await _outZen.InvokeVoidAsync("initCrowdChart", "crowdChart");
        }

        private void ClickInfo(int id)
        {
            if (id <= 0)
            {
                Console.WriteLine("[TrafficConditionView] Ignoring Info click: id <= 0 (payload without ID ?)");
                return;
            }
            SelectedId = id;
            Console.WriteLine($"[TrafficConditionView] SelectedId = {SelectedId}");
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (currentIndex < allTrafficConditions.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        private IEnumerable<ClientTrafficConditionDTO> FilterTraffic(IEnumerable<ClientTrafficConditionDTO> source)
            => FilterTrafficCondition(source);

        private IEnumerable<ClientTrafficConditionDTO> FilterTrafficCondition(IEnumerable<ClientTrafficConditionDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (!string.IsNullOrEmpty(x.IncidentType) && x.IncidentType.Contains(q, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(x.Message) && x.Message.Contains(q, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(x.CongestionLevel) && x.CongestionLevel.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !_onlyRecent || x.DateCondition >= cutoff);
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





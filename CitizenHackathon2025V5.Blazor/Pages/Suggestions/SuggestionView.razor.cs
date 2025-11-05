using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025.Shared.StaticConfig.Constants;
using CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionView : IAsyncDisposable
    {
    #nullable disable
        [Inject]
        public HttpClient Client { get; set; } 
        [Inject] public SuggestionService SuggestionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;
        public List<ClientSuggestionDTO> Suggestions { get; set; }
        private List<ClientSuggestionDTO> allSuggestions = new();
        private List<ClientSuggestionDTO> visibleSuggestions = new();
        private int currentIndex = 0;
        private const int PageSize = 100;
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
            var fetched = (await SuggestionService.GetLatestSuggestionAsync())?.ToList()
                        ?? new List<ClientSuggestionDTO>();

            Suggestions = fetched;
            allSuggestions = fetched;
            visibleSuggestions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{apiBaseUrl}{SuggestionHubMethods.HubPath}", options =>
                {
                    // If your hub is later protected, provide a token
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // Handlers
            hubConnection.On<ClientSuggestionDTO>(SuggestionHubMethods.ToClient.ReceiveSuggestion, async dto =>
            {
                void Upsert(List<ClientSuggestionDTO> list)
                {
                    var i = list.FindIndex(c => c.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(Suggestions);
                Upsert(allSuggestions);

                var j = visibleSuggestions.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleSuggestions[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateSuggestionMarker",
                    dto.Id.ToString(),
                    dto.Reason,
                    dto.SuggestedAlternatives,
                    dto.OriginalPlace,
                    new { title = dto.OriginalPlace, description = $"Maj {dto.DateSuggestion:HH:mm:ss}" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On(SuggestionHubMethods.ToClient.NewSuggestion, () =>
            {
                // Optional: slight UI refresh
                InvokeAsync(StateHasChanged);
            });
            //hubConnection.On(SuggestionHubMethods.ToClient.NewSuggestion, () =>
            //{
            //    Console.WriteLine("NewSuggestion (no payload)");
            //    InvokeAsync(StateHasChanged);
            //});

            //// Server -> Client (avec payload SuggestionDTO)
            //hubConnection.On<SuggestionDTO>(SuggestionHubMethods.ToClient.ReceiveSuggestion, dto =>
            //{
            //    Console.WriteLine($"ReceiveSuggestion: {dto?.Title}");
            //    // TODO: upsert dans ta liste + StateHasChanged
            //});

            try 
            { 
                await hubConnection.StartAsync();
                //await hubConnection.InvokeAsync(SuggestionHubMethods.FromClient.RefreshSuggestion);
                //var suggestion = new SuggestionDTO { /* init … */ };
                //await hubConnection.InvokeAsync(SuggestionHubMethods.FromClient.SendSuggestion, suggestion);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[SuggestionView] Hub start failed: {ex.Message}"); }
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
            try { await JS.InvokeVoidAsync("disposeEarth", _canvasId); } catch { }
        }
    }
}


























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





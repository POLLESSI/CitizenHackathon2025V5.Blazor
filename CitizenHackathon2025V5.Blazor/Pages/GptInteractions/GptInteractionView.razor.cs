using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView : IAsyncDisposable
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public GptInteractionService GptInteractionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _outZen;

        public List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();
        private List<ClientGptInteractionDTO> allGptInteractions = new();      
        private List<ClientGptInteractionDTO> visibleGptInteractions = new();  
        private int currentIndex = 0;
        private const int PageSize = 20;
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            var fetched = (await GptInteractionService.GetAllInteractions())?.ToList() ?? new();
            GptInteractions = fetched;
            allGptInteractions = fetched;
            visibleGptInteractions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase.TrimEnd('/');
            var hubPath = "/hubs/gpthub";
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

            //hubConnection = new HubConnectionBuilder()
            //    .WithUrl(apiBaseUrl.TrimEnd('/') + GptInteractionHubMethods.HubPath, options =>
            //    {
            //        options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
            //    })
            //    .WithAutomaticReconnect()
            //    .Build();

            // Handlers
            hubConnection.On<ClientGptInteractionDTO>("RefreshGPT", async dto =>
            {
                if (dto is null) return;
                void Upsert(List<ClientGptInteractionDTO> list)
                {
                    var i = list.FindIndex(g => g.Id == dto.Id);
                    if (i >= 0) list[i] = dto; else list.Add(dto);
                }

                Upsert(GptInteractions);
                Upsert(allGptInteractions);

                var j = visibleGptInteractions.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleGptInteractions[j] = dto;

                await JS.InvokeVoidAsync("window.OutZenInterop.addOrUpdateEventMarker",
                    dto.Id.ToString(), dto.Prompt ?? "", dto.Response ?? "", dto.CreatedAt,
                    new { title = dto.Prompt ?? "", description = $"Maj {dto.CreatedAt:HH:mm:ss}" });

                await InvokeAsync(StateHasChanged);
            });

            hubConnection.On<int>("GptInteractionArchived", async id =>
            {
                GptInteractions.RemoveAll(c => c.Id == id);
                allGptInteractions.RemoveAll(c => c.Id == id);
                visibleGptInteractions.RemoveAll(c => c.Id == id);

                await JS.InvokeVoidAsync("window.OutZenInterop.removeMarker", id.ToString());
                await InvokeAsync(StateHasChanged);
            });

            //hubConnection.On<string>(GptInteractionHubMethods.ToClient.NotifyNewGpt, payload =>
            //{
            //    Console.WriteLine($"GPT notify: {payload}");
            //    InvokeAsync(StateHasChanged);
            //});

            //// Client -> Serveur
            //await hubConnection.InvokeAsync(GptInteractionHubMethods.FromClient.RefreshGpt, "refresh now");

            try { await hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[GptInteractionView] Hub start failed: {ex.Message}"); }
        }
        private void LoadMoreItems()
        {
            var next = allGptInteractions.Skip(currentIndex).Take(PageSize).ToList();
            visibleGptInteractions.AddRange(next);
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

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            if (scrollTop + clientHeight >= scrollHeight - 5)
            {
                if (currentIndex < allGptInteractions.Count)
                {
                    LoadMoreItems();
                    await InvokeAsync(StateHasChanged);
                }
            }
        }
        private IEnumerable<ClientGptInteractionDTO> FilterGpt(IEnumerable<ClientGptInteractionDTO> source)
            => FilterGptInteraction(source);

        private IEnumerable<ClientGptInteractionDTO> FilterGptInteraction(IEnumerable<ClientGptInteractionDTO> source)
        {
            var q = _q?.Trim();
            var cutoff = DateTime.UtcNow.AddHours(-6);

            return source
                .Where(x => string.IsNullOrEmpty(q)
                            || (!string.IsNullOrEmpty(x.Prompt) && x.Prompt.Contains(q, StringComparison.OrdinalIgnoreCase))
                            || (!string.IsNullOrEmpty(x.Response) && x.Response.Contains(q, StringComparison.OrdinalIgnoreCase)))
                .Where(x => !_onlyRecent || x.CreatedAt >= cutoff);
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





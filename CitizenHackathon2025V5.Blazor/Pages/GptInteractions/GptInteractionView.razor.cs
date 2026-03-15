using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Enums;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Pages.Shared;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System.Net.Http.Json;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public GptInteractionService GptInteractionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }
        protected override string MapId => "leafletMap-gptinteractionview";
        protected override string ScopeKey => "gptinteractionview";
        protected override int DefaultZoom => 14;
        protected override (double lat, double lng) DefaultCenter => (50.29, 4.99);
        protected override OutZenMarkerPolicy MarkerPolicy => OutZenMarkerPolicy.OnlyPrefix;
        //protected override string AllowedMarkerPrefix => "gpt:";
        protected override bool ClearAllOnMapReady => true;
        public List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();
        private List<ClientGptInteractionDTO> allGptInteractions = new();
        private List<ClientGptInteractionDTO> visibleGptInteractions = new();

        private int currentIndex = 0;
        private const int PageSize = 20;
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        private ElementReference ScrollContainerRef;
        private string NewPrompt { get; set; } = string.Empty;
        private string _q;
        private bool _onlyRecent;
        private bool _disposed;

        protected override async Task OnInitializedAsync()
        {
            var fetched = (await GptInteractionService.GetAllInteractions())?.ToList() ?? new();
            GptInteractions = fetched;
            allGptInteractions = fetched;
            visibleGptInteractions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);

            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

            hubConnection = new HubConnectionBuilder()
                .WithUrl($"{hubBaseUrl}/hubs/gptHub", options =>
                {
                    options.AccessTokenProvider = async () =>
                        await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<ClientGptInteractionDTO>("ReceiveGptResponse", async dto =>
            {
                if (dto is null) return;

                void Upsert(List<ClientGptInteractionDTO> list)
                {
                    var i = list.FindIndex(g => g.Id == dto.Id);
                    if (i >= 0) list[i] = dto;
                    else list.Add(dto);
                }

                Upsert(GptInteractions);
                Upsert(allGptInteractions);

                var j = visibleGptInteractions.FindIndex(c => c.Id == dto.Id);
                if (j >= 0) visibleGptInteractions[j] = dto;

                await InvokeAsync(StateHasChanged);
            });

            try
            {
                await hubConnection.StartAsync();
                Console.WriteLine("[GptInteractionView] Hub started.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GptInteractionView] Hub start failed: {ex}");
            }
        }
        private void LoadMoreItems()
        {
            var next = allGptInteractions.Skip(currentIndex).Take(PageSize).ToList();
            visibleGptInteractions.AddRange(next);
            currentIndex += next.Count;
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

        private async Task HandleAskGpt()
        {
            Console.WriteLine($"[GPT] HandleAskGpt called. Raw prompt = '{NewPrompt}'");
            Console.WriteLine($"[GPT] Client.BaseAddress = {Client.BaseAddress}");

            if (string.IsNullOrWhiteSpace(NewPrompt))
            {
                Console.WriteLine("[GPT] Prompt is empty -> request cancelled.");
                return;
            }

            try
            {
                var request = new
                {
                    Prompt = NewPrompt.Trim(),
                    Latitude = 50.0,
                    Longitude = 4.5
                };

                Console.WriteLine("[GPT] Sending POST api/gpt/ask-mistral ...");

                var response = await Client.PostAsJsonAsync("api/gpt/ask-mistral", request);

                Console.WriteLine($"[GPT] Response status = {(int)response.StatusCode} {response.StatusCode}");

                var raw = await response.Content.ReadAsStringAsync();
                Console.WriteLine($"[GPT] Response body = {raw}");

                response.EnsureSuccessStatusCode();

                var fetched = await GptInteractionService.GetAllInteractions();
                GptInteractions = fetched?.ToList() ?? new();
                allGptInteractions = GptInteractions;
                visibleGptInteractions.Clear();
                currentIndex = 0;
                LoadMoreItems();

                NewPrompt = string.Empty;
                await InvokeAsync(StateHasChanged);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GPT] HandleAskGpt failed: {ex}");
            }
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





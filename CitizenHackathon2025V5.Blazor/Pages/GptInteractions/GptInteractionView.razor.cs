using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView : IAsyncDisposable
    {
#nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public GptInteractionService GptInteractionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }
        [Inject] public IHubUrlBuilder HubUrls { get; set; }

        public List<ClientGptInteractionDTO> GptInteractions { get; set; } = new();
        private List<ClientGptInteractionDTO> allGptInteractions = new();
        private List<ClientGptInteractionDTO> visibleGptInteractions = new();

        private int currentIndex = 0;
        private const int PageSize = 20;
        private string _speedId = $"speedRange-{Guid.NewGuid():N}";

        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        private ElementReference ScrollContainerRef;
        private string _q;
        private bool _onlyRecent;

        protected override async Task OnInitializedAsync()
        {
            // 1) Initial REST
            var fetched = (await GptInteractionService.GetAllInteractions())?.ToList() ?? new();
            GptInteractions = fetched;
            allGptInteractions = fetched;
            visibleGptInteractions.Clear();
            currentIndex = 0;
            LoadMoreItems();

            await InvokeAsync(StateHasChanged);

            // 2) SignalR GPT (only to refresh the list)
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');

            var url = HubUrls.Build(HubPaths.GptInteraction);

            hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () =>
                        await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            // Handlers
            hubConnection.On<ClientGptInteractionDTO>(GptInteractionHubMethods.ToClient.NotifyNewGpt, async dto =>
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

            hubConnection.On<int>("GptInteractionArchived", async id =>
            {
                GptInteractions.RemoveAll(c => c.Id == id);
                allGptInteractions.RemoveAll(c => c.Id == id);
                visibleGptInteractions.RemoveAll(c => c.Id == id);
                await InvokeAsync(StateHasChanged);
            });

            try
            {
                await hubConnection.StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[GptInteractionView] Hub start failed: {ex.Message}");
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

        private void ToggleRecent() => _onlyRecent = !_onlyRecent;

        public async ValueTask DisposeAsync()
        {
            if (hubConnection is not null)
            {
                try { await hubConnection.StopAsync(); } catch { }
                try { await hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}


































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





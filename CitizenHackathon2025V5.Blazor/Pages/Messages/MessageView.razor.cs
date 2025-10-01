using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Messages
{

    public partial class MessageView : IAsyncDisposable
    {
#nullable disable
        //[Inject] public MessageService MessageService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IHubTokenService HubTokenService { get; set; }
        [Inject] public IHttpClientFactory HttpFactory { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private const string ApiBase = "https://localhost:7254";
        private IJSObjectReference? _message;
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            //var fetched = (await MessageService.GetAllMessagesAsync()).ToList();
            //Messages = fetched;
            //allMessages = fetched;
            //visibleMessages.Clear();
            //currentIndex = 0;
            //LoadMoreItems();
            // 2) SignalR (Absolute URL on API side)
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? ApiBase.TrimEnd('/');
            var hubPath = "/hubs/messageHub";
            var hubUrl = BuildHubUrl(apiBaseUrl, hubPath);
            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        // Get your JWT here (via IAuthService, etc.)
                        var token = await Auth.GetAccessTokenAsync();
                        return token ?? string.Empty;
                    };
                })
                .WithAutomaticReconnect()
                .Build();
            //// AISuggestionHub
            //hubConnection = new HubConnectionBuilder()
            //    .WithUrl(apiBaseUrl + TourismeHubMethods.HubPath)
            //    .WithAutomaticReconnect()
            //    .Build();

            //hubConnection.On<string>(TourismeHubMethods.ToClient.SuggestionsUpdated, json =>
            //{
            //    // parse/apply
            //    InvokeAsync(StateHasChanged);
            //});

            //// NotificationHub
            //notifConnection = new HubConnectionBuilder()
            //    .WithUrl(apiBaseUrl + NotificationHubMethods.HubPath)
            //    .WithAutomaticReconnect()
            //    .Build();

            //notifConnection.On<string>(NotificationHubMethods.ToClient.Notify, msg =>
            //{
            //    Console.WriteLine($"[NOTIF] {msg}");
            //}); 

            // handlers...
            //hubConnection.On<ClientMessageDTO>("ReceiveMessageUpdate", async dto =>
            //{
            //    void Upsert(List<ClientMessageDTO> list)
            //    {
            //        var existing = list.FirstOrDefault(x => x.Id == dto.Id);
            //        if (existing != null)
            //        {
            //            // Update existing
            //            var index = list.IndexOf(existing);
            //            list[index] = dto;
            //        }
            //        else
            //        {
            //            // Add new
            //            list.Insert(0, dto); // Insert at the top
            //        }
            //    }
            //    Upsert(Messages);
            //    Upsert(allMessages);
            //    await InvokeAsync(StateHasChanged);
            //});
            await hubConnection.StartAsync();
            // JS init
            try
            {
                _message = await JS.InvokeAsync<IJSObjectReference>("import", "./_content/CitizenHackathon2025V5.Blazor.Client/js/message.js");
                if (_message is not null)
                {
                    await _message.InvokeVoidAsync("initializeMessage", DotNetObjectReference.Create(this), ScrollContainerRef);
                }
            }
            catch { /* ignore */ }
        }

        private static string BuildHubUrl(string baseUrl, string path)
        {
            var b = baseUrl.TrimEnd('/');
            var p = path.TrimStart('/'); // ex: "hubs/crowdHub"

            // If the base already ends with "/hubs" AND the path begins with "hubs/", we avoid the duplicate
            if (b.EndsWith("/hubs", StringComparison.OrdinalIgnoreCase) &&
                p.StartsWith("hubs/", StringComparison.OrdinalIgnoreCase))
            {
                p = p.Substring("hubs/".Length); // => "crowdHub"
            }

            return $"{b}/{p}";
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_message is not null)
                {
                    // optional: if you have a destroy function in the module
                    await _message.DisposeAsync();
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

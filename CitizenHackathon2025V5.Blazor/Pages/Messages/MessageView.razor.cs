using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Messages
{
    public partial class MessageView : IAsyncDisposable
    {
#nullable disable
        [Inject] public MessageService MessageService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IJSRuntime JS { get; set; }
        [Inject] public IConfiguration Config { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        private List<ClientMessageDTO> _messages = new();
        private string _newContent;
        private HubConnection _hubConnection;
        private ElementReference ScrollContainerRef;

        protected override async Task OnInitializedAsync()
        {
            // 1) REST initial
            _messages = await MessageService.GetLatestAsync(100);

            // 2) SignalR
            var apiBaseUrl = Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254";
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');
            var hubPath = HubPaths.Message.Trim('/'); // "messageHub"
            var url = $"{hubBaseUrl}/hubs/{hubPath}";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ClientMessageDTO>("ReceiveMessageUpdate", async dto =>
            {
                if (dto is null) return;

                _messages.Insert(0, dto);
                await InvokeAsync(StateHasChanged);
            });

            try
            {
                await _hubConnection.StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MessageView] Hub start failed: {ex.Message}");
            }
        }

        private async Task SendAsync()
        {
            var content = _newContent?.Trim();
            if (string.IsNullOrEmpty(content))
                return;

            var created = await MessageService.PostAsync(content);
            if (created is not null)
            {
                // Optionnel : l’insert local est déjà fait par SignalR; ici on peut ne rien faire
                _newContent = string.Empty;
            }
        }

        private async Task HandleScroll()
        {
            var scrollTop = await JS.InvokeAsync<int>("getScrollTop", ScrollContainerRef);
            var scrollHeight = await JS.InvokeAsync<int>("getScrollHeight", ScrollContainerRef);
            var clientHeight = await JS.InvokeAsync<int>("getClientHeight", ScrollContainerRef);

            // Pour l’instant, pas de lazy-load (tu pourras réintroduire plus tard)
        }

        public async ValueTask DisposeAsync()
        {
            if (_hubConnection is not null)
            {
                try { await _hubConnection.StopAsync(); } catch { }
                try { await _hubConnection.DisposeAsync(); } catch { }
            }
        }
    }
}












































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
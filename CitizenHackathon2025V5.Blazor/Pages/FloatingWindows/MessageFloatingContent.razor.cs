using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.FloatingWindows
{
    public partial class MessageFloatingContent
    {
        private List<ClientMessageDTO> _messages = new();
        private string _newContent = "";
        private HubConnection? _hubConnection;
        private ElementReference ScrollContainerRef;

        protected override async Task OnInitializedAsync()
        {
            _messages = await MessageService.GetLatestAsync(100) ?? new();

            var apiBaseUrl = (Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254");
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');
            var hubPath = HubPaths.Message.Trim('/');
            var url = $"{hubBaseUrl}/hubs/{hubPath}";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                    options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets
                                      | Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents;
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ClientMessageDTO>("ReceiveMessageUpdate", async dto =>
            {
                if (dto is null) return;
                _messages.Insert(0, dto);
                await InvokeAsync(StateHasChanged);
            });

            try { await _hubConnection.StartAsync(); }
            catch (Exception ex) { Console.Error.WriteLine($"[MessageFloatingContent] Hub start failed: {ex.Message}"); }
        }

        private async Task SendAsync()
        {
            var content = _newContent?.Trim();
            if (string.IsNullOrWhiteSpace(content)) return;

            var created = await MessageService.PostAsync(content);
            if (created is not null)
                _newContent = "";
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
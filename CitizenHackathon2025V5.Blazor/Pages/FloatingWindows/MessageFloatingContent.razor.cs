using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using CitizenHackathon2025V5.Blazor.Client.Services;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.FloatingWindows
{
    public partial class MessageFloatingContent : IAsyncDisposable
    {
        private List<ClientMessageDTO> _messages = new();
        private string _newContent = "";
        private HubConnection? _hubConnection;
        private ElementReference ScrollContainerRef;

        private bool _historyCollapsed = true;   // Hide by default
        private bool _isSending;
        private const int LatestVisibleCount = 2;

        [Inject] public MessageService MessageService { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        [Inject] public IJSRuntime JS { get; set; } = default!;
        [Inject] public IConfiguration Config { get; set; } = default!;
        [Inject] public IAuthService Auth { get; set; } = default!;

        protected override async Task OnInitializedAsync()
        {
            try
            {
                _messages = await MessageService.GetLatestAsync(100) ?? new();
                _messages = _messages
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MessageFloatingContent] Initial load failed: {ex.Message}");
                _messages = new();
            }

            var apiBaseUrl = (Config["ApiBaseUrl"]?.TrimEnd('/') ?? "https://localhost:7254");
            var hubBaseUrl = (Config["SignalR:HubBase"] ?? apiBaseUrl).TrimEnd('/');
            var hubPath = HubPaths.Message.Trim('/');
            var url = $"{hubBaseUrl}/hubs/{hubPath}";

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(url, options =>
                {
                    options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
                    options.Transports =
                        Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets |
                        Microsoft.AspNetCore.Http.Connections.HttpTransportType.ServerSentEvents;
                })
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<ClientMessageDTO>("ReceiveMessageUpdate", async dto =>
            {
                if (dto is null) return;

                var idx = _messages.FindIndex(x => x.Id == dto.Id);
                if (idx >= 0)
                    _messages[idx] = dto;
                else
                    _messages.Insert(0, dto);

                _messages = _messages
                    .OrderByDescending(x => x.CreatedAt)
                    .ToList();

                await InvokeAsync(StateHasChanged);
            });

            try
            {
                await _hubConnection.StartAsync();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[MessageFloatingContent] Hub start failed: {ex.Message}");
            }
        }

        private async Task SendAsync()
        {
            var content = _newContent?.Trim();
            if (string.IsNullOrWhiteSpace(content) || _isSending)
                return;

            try
            {
                _isSending = true;

                var created = await MessageService.PostAsync(content);
                if (created is not null)
                {
                    var idx = _messages.FindIndex(x => x.Id == created.Id);
                    if (idx >= 0)
                        _messages[idx] = created;
                    else
                        _messages.Insert(0, created);

                    _messages = _messages
                        .OrderByDescending(x => x.CreatedAt)
                        .ToList();
                }

                _newContent = string.Empty;
            }
            finally
            {
                _isSending = false;
                await InvokeAsync(StateHasChanged);
            }
        }

        private void ToggleHistory()
        {
            _historyCollapsed = !_historyCollapsed;
        }

        private static string BuildSource(ClientMessageDTO m)
        {
            var source = string.IsNullOrWhiteSpace(m.SourceType) ? "N/A" : m.SourceType.Trim();
            var related = string.IsNullOrWhiteSpace(m.RelatedName) ? "" : $" • {m.RelatedName.Trim()}";
            return source + related;
        }

        private static string ShortenContent(string? text, int max = 180)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "— Empty content —";

            var normalized = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return normalized.Length <= max ? normalized : normalized[..max] + "…";
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
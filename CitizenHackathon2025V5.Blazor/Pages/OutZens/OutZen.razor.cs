using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.OutZens
{
    public partial class OutZen
    {
        private ClientCrowdInfoDTO? crowdInfo;
        private List<ClientSuggestionDTO>? suggestions;
        private OutZenSignalRService? _signalR;

        protected override async Task OnInitializedAsync()
        {
            // ?? Use the factory
            _signalR = await OutZenFactory.CreateAsync();

            // ?? Subscribe to events
            _signalR.OnCrowdInfoUpdated += dto =>
            {
                crowdInfo = dto;
                InvokeAsync(StateHasChanged);
            };

            _signalR.OnSuggestionsUpdated += list =>
            {
                suggestions = list;
                InvokeAsync(StateHasChanged);
            };

            // ?? Initialize the connection
            await _signalR.InitializeOutZenAsync();
            // // C# Blazor side: subscription via stable names
            // var conn = new HubConnectionBuilder()
            //     .WithUrl(apiBase + OutZenEvents.HubPath, options =>
            //     {
            //         options.AccessTokenProvider = async () => await Auth.GetAccessTokenAsync() ?? string.Empty;
            //     })
            //     .WithAutomaticReconnect()
            //     .Build();

            // conn.On<object>(OutZenEvents.ToClient.CrowdInfoUpdated, dto =>
            // {
            //     // Upsert + StateHasChanged
            // });
            // conn.On<object>(OutZenEvents.ToClient.WeatherUpdated, wx => { /* ... */ });

            // await conn.StartAsync();
            // await conn.InvokeAsync("JoinEventGroup", eventId);
        }
        private static int GetDensityFromLevel(string? level)
        {
            if (int.TryParse(level, out var n))
                return GetDensityFromLevel(n);

            return level?.ToLowerInvariant() switch
            {
                "low" => 25,
                "medium" => 50,
                "high" => 75,
                "critical" => 90,
                _ => 50
            };
        }
        private static int GetDensityFromLevel(int n)
        {
            if (n <= 3) return 25;
            if (n <= 6) return 50;
            if (n <= 8) return 75;
            return 90;
        }

        public async ValueTask DisposeAsync()
        {
            if (_signalR != null)
            {
                await _signalR.DisposeAsync();
            }
        }
    }
}


























































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
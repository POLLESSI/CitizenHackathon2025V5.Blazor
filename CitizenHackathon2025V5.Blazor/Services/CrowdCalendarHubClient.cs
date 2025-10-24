// Services/CrowdCalendarHubClient.cs
using CitizenHackathon2025V5.Blazor.Client.Shared.StaticConfig.Constants;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class CrowdCalendarHubClient : IAsyncDisposable
    {
        private readonly NavigationManager _nav;
        private HubConnection? _hub;

        public event Func<Task>? OnCalendarUpdated;

        public CrowdCalendarHubClient(NavigationManager nav)
        {
            _nav = nav;
        }

        public async Task StartAsync()
        {
            _hub = new HubConnectionBuilder()
                .WithUrl(_nav.ToAbsoluteUri(CrowdCalendarHubMethods.HubPath))
                .WithAutomaticReconnect()
                .Build();

            _hub.On<object>(CrowdCalendarHubMethods.ReceiveCalendarUpdated, async _ =>
            {
                if (OnCalendarUpdated is not null) await OnCalendarUpdated.Invoke();
            });

            await _hub.StartAsync();
        }

        public async ValueTask DisposeAsync()
        {
            if (_hub is not null)
            {
                await _hub.DisposeAsync();
            }
        }
    }
}
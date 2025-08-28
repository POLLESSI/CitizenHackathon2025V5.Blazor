using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficSignalRService
    {
        private HubConnection? _hubConnection;
        private readonly NavigationManager _navigation;
        private readonly IJSRuntime _js;

        public event Action<string>? OnTrafficInfoUpdated;
        public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

        public TrafficSignalRService(NavigationManager navigation, IJSRuntime js)
        {
            _navigation = navigation;
            _js = js;
        }

        public async Task StartAsync()
        {
            if (_hubConnection is not null)
                return;

            _hubConnection = new HubConnectionBuilder()
                .WithUrl(_navigation.ToAbsoluteUri("/hubs/wazetraffic"))
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<WazeFeed>("ReceiveTraffic", feed =>
            {
                var info = $"{feed.Jams.Count} slowdowns, {feed.Alerts.Count} alerts";
                OnTrafficInfoUpdated?.Invoke(info);
            });

            try
            {
                await _hubConnection.StartAsync();
                Console.WriteLine("✅ Connected to SignalR traffic hub.");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"❌ Failed to connect to SignalR traffic hub: {ex.Message}");
            }
        }

        public async Task StopAsync()
        {
            if (_hubConnection != null)
            {
                await _hubConnection.StopAsync();
                await _hubConnection.DisposeAsync();
                _hubConnection = null;
            }
        }
    }
}























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
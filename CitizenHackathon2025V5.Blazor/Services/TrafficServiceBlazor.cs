using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using System.Net.Http;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class TrafficServiceBlazor
    {
        private readonly HubConnection _hubConnection;
        public event Action<List<ClientTrafficEventDTO>>? OnTrafficReceived;

        public TrafficServiceBlazor(NavigationManager nav)
        {
            _hubConnection = new HubConnectionBuilder()
                .WithUrl(nav.ToAbsoluteUri("/hubs/trafficHub"))
                .WithAutomaticReconnect()
                .Build();

            _hubConnection.On<List<ClientTrafficEventDTO>>("ReceiveTraffic", (trafficEvents) =>
            {
                OnTrafficReceived?.Invoke(trafficEvents);
            });
        }

        public async Task StartAsync()
        {
            if (_hubConnection.State == HubConnectionState.Disconnected)
                await _hubConnection.StartAsync();
        }

        public async Task RequestTraffic()
        {
            await _hubConnection.SendAsync("SendTrafficUpdate");
        }
    }
}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





using CitizenHackathon2025V5.Blazor.Client.DTOs;
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
        //public async Task<IEnumerable<TrafficConditionModel?>> GetLatestTrafficConditionAsync()
        //{
        //    var response = await _httpClient.GetAsync("api/TrafficConditionDTO/latest");
        //    if (response.IsSuccessStatusCode)
        //    {
        //        return await response.Content.ReadFromJsonAsync<IEnumerable<TrafficConditionModel?>>();
        //    }
        //    return Enumerable.Empty<TrafficConditionModel?>();
        //}
        //public async Task<TrafficConditionModel> SaveTrafficConditionAsync(TrafficConditionModel @TrafficConditionDTO)
        //{
        //    var response = await _httpClient.PostAsJsonAsync("api/TrafficConditionDTO", @TrafficConditionDTO);
        //    if (response.IsSuccessStatusCode)
        //    {
        //        return await response.Content.ReadFromJsonAsync<TrafficConditionModel>();
        //    }
        //    throw new Exception("Failed to save traffic condition");
        //}
        //public TrafficConditionModel? UpdateTrafficCondition(TrafficConditionModel @TrafficConditionDTO)
        //{
        //    // This method is not implemented in the original code.
        //    // You can implement it based on your requirements.
        //    throw new NotImplementedException("UpdateTrafficCondition method is not implemented.");
        //}
    }
}













































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





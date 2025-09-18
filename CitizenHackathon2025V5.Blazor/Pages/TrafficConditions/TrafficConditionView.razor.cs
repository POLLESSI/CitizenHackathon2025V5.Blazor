using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionView
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public TrafficConditionService TrafficConditionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        public List<TrafficConditionModel> TrafficConditions { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            TrafficConditions = new List<TrafficConditionModel>();

            await GetTrafficCondition();

            hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/hubs/trafficHub"))
                .WithAutomaticReconnect()
                .Build();

            await hubConnection.StartAsync();

            using (var message = await Client.GetAsync("TrafficCondition/Latest")) 
            { 
                //...
            } // /api/trafficcondition/latest
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetTrafficCondition()
        {
            using (HttpResponseMessage message = await Client.GetAsync("TrafficCondition/Latest"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    TrafficConditions = JsonConvert.DeserializeObject<List<TrafficConditionModel>>(json);
                    // Process traffic conditions as needed
                }
                else
                {
                    // Handle error response
                }
            }
        }
    }
}































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





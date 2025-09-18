using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Events
{
    public partial class EventView
    {
    #nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public EventService EventService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        public List<EventModel> Events { get; set; }
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            Events = new List<EventModel>();

            await GetEvent();

            hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/hubs/eventHub"))
                .WithAutomaticReconnect()
                .Build();
            await hubConnection.StartAsync();

            using (var message = await Client.GetAsync("Event/Latest")) 
            { 
                //... 
            } // baseAddress = /api/
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetEvent()
        {
            using (HttpResponseMessage message = await Client.GetAsync("Event/Latest"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    Events = JsonConvert.DeserializeObject<List<EventModel>>(json);
                    // Process event as needed
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





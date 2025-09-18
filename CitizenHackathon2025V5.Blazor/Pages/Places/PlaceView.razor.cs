using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Places
{
    public partial class PlaceView
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public PlaceService PlaceService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        public List<PlaceModel> Places { get; set; }
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            Places = new List<PlaceModel>();

            await GetPlace();

            hubConnection = new HubConnectionBuilder()
                .WithUrl(Navigation.ToAbsoluteUri("/hubs/placeHub"))
                .WithAutomaticReconnect()
                .Build();
            await hubConnection.StartAsync();

            using (var message = await Client.GetAsync("Place/Latest")) 
            { 
                //... 
            } // /api/place
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetPlace()
        {
            using (HttpResponseMessage message = await Client.GetAsync("Place/Latest"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    Places = JsonConvert.DeserializeObject<List<PlaceModel>>(json);
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

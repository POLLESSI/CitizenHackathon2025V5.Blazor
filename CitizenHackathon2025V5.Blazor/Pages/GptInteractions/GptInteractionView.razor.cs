using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.GptInteractions
{
    public partial class GptInteractionView
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public GptInteractionService GptInteractionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        public List<GptInteractionModel> GptInteractions { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            GptInteractions = new List<GptInteractionModel>();

            await GetGptInteractions();

            hubConnection = new HubConnectionBuilder()
                .WithUrl(new Uri("https://localhost:7254/hubs/gptinteractionHub"))
                .WithAutomaticReconnect()
                .Build();
            await hubConnection.StartAsync();

            using (var message = await Client.GetAsync("Gpt/all")) 
            {
                //... 
            } // /api/gptinteraction
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetGptInteractions()
        {
            using (HttpResponseMessage message = await Client.GetAsync("Gpt/all"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    GptInteractions = JsonConvert.DeserializeObject<List<GptInteractionModel>>(json);
                    // Process GptInteractions as needed
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





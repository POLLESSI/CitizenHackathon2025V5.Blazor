using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionView
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public SuggestionService SuggestionService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        public List<SuggestionModel> Suggestions { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            Suggestions = new List<SuggestionModel>();

            await GetSuggestion();

            hubConnection = new HubConnectionBuilder()
    .WithUrl(new Uri("https://localhost:7254/hubs/suggestionHub"))
    .WithAutomaticReconnect()
    .Build();
            await hubConnection.StartAsync();

            using (var message = await Client.GetAsync("Suggestions/all")) 
            { 
                //... 
            } // /api/suggestion
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetSuggestion()
        {
            using (HttpResponseMessage message = await Client.GetAsync("Suggestions/all"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    Suggestions = JsonConvert.DeserializeObject<List<SuggestionModel>>(json);
                    // Process suggestions as needed
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





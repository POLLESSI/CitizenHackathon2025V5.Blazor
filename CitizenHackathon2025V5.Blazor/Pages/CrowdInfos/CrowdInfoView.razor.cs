using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.CrowdInfos
{
    public partial class CrowdInfoView
    {
#nullable disable
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject] public CrowdInfoService CrowdInfoService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }

        public List<CrowdInfoModel> CrowdInfos { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            CrowdInfos = new List<CrowdInfoModel>();

            await GetCrowdInfo();

            hubConnection = new HubConnectionBuilder()
                .WithUrl(new Uri("https://localhost:7254/hubs/crowdinfoHub"))
                .Build();

            await hubConnection.StartAsync();
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetCrowdInfo()
        {
            using (HttpResponseMessage message = await Client.GetAsync("crowdinfo"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    CrowdInfos = JsonConvert.DeserializeObject<List<CrowdInfoModel>>(json);
                    // Process crowdInfo as needed
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
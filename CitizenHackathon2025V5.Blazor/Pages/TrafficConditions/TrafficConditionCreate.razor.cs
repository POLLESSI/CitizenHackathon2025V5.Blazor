using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.TrafficConditions
{
    public partial class TrafficConditionCreate
    {
        [Inject]
        public HttpClient Client { get; set; }  // Injection HttpClient
        [Inject]
        public NavigationManager Navigation { get; set; }
        private TrafficConditionModel NewTrafficCondition { get; set; } = new TrafficConditionModel();

        protected override async Task OnInitializedAsync()
        {
            NewTrafficCondition = new TrafficConditionModel();
        }
        public async Task submit()
        {
            string json = JsonConvert.SerializeObject(NewTrafficCondition);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using (HttpResponseMessage response = await Client.PostAsync("trafficcondition", content))
            {
                if (!response.IsSuccessStatusCode) { Console.WriteLine(response.Content); }
            }
        }
    }
}










































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
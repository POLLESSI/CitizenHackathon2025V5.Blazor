using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.Suggestions
{
    public partial class SuggestionCreate
    {
        [Inject] public HttpClient Client { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        private SuggestionModel NewSuggestion { get; set; } = new SuggestionModel();

        protected override Task OnInitializedAsync()
        {
            NewSuggestion = new SuggestionModel();
            return Task.CompletedTask;
        }
        public async Task submit()
        {
            string json = JsonConvert.SerializeObject(NewSuggestion);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using (HttpResponseMessage response = await Client.PostAsync("suggestion", content))
            {
                if (!response.IsSuccessStatusCode) { Console.WriteLine(response.Content); }
            }
        }
    }
}





































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





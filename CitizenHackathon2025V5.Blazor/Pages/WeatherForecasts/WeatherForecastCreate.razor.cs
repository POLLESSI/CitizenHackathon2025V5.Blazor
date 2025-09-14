using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Text;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastCreate
    {
        [Inject] public HttpClient Client { get; set; } = default!;
        [Inject] public NavigationManager Navigation { get; set; } = default!;
        private WeatherForecastModel NewWeatherForecast { get; set; } = new WeatherForecastModel();

        protected override Task OnInitializedAsync()
        {
            NewWeatherForecast = new WeatherForecastModel();
            return Task.CompletedTask;
        }
        public async Task submit()
        {
            string json = JsonConvert.SerializeObject(NewWeatherForecast);
            HttpContent content = new StringContent(json, Encoding.UTF8, "application/json");
            using (HttpResponseMessage response = await Client.PostAsync("WeatherForecastDTO", content))
            {
                if (!response.IsSuccessStatusCode) { Console.WriteLine(response.Content); }
            }
        }
    }
}







































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





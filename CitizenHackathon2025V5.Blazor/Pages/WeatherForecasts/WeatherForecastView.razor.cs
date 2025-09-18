using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Newtonsoft.Json;
using System.Runtime.Intrinsics.Arm;
using System.Text.Json.Serialization;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastView
    {
    #nullable disable
        [Inject]
        public HttpClient Client { get; set; }  
        [Inject] public WeatherForecastService WeatherForecastService { get; set; }
        [Inject] public NavigationManager Navigation { get; set; }
        [Inject] public IAuthService Auth { get; set; }

        public List<WeatherForecastModel> WeatherForecasts { get; set; } = new();
        public int SelectedId { get; set; }
        public HubConnection hubConnection { get; set; }

        protected override async Task OnInitializedAsync()
        {
            allWeatherForecasts = await WeatherForecastService.GetHistoryAsync(limit: 200);
            LoadMoreItems();

            var apiBaseUrl = "https://localhost:7254";
            var hubUrl = $"{apiBaseUrl.TrimEnd('/')}/hubs/weatherforecastHub";
            hubConnection = new HubConnectionBuilder()
                .WithUrl(hubUrl, options =>
                {
                    options.AccessTokenProvider = async () =>
                    {
                        // Get your JWT here (via IAuthService, etc.)
                        var token = await Auth.GetAccessTokenAsync();
                        return token;
                    };
                })
                .WithAutomaticReconnect()
                .Build();

            hubConnection.On<ClientWeatherForecastDTO>("NewWeatherForecast", dto => { /* update UI */ });

            await hubConnection.StartAsync();
        }
        private void ClickInfo(int id) => SelectedId = id;

        private async Task GetWeatherForecast()
        {
            using (HttpResponseMessage message = await Client.GetAsync("WeatherForecast/All"))
            {
                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync();
                    WeatherForecasts = JsonConvert.DeserializeObject<List<WeatherForecastModel>>(json);
                    // Process weather forecasts as needed
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





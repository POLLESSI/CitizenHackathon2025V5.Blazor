using CitizenHackathon2025.Blazor.DTOs;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class Weather
    {
        private IReadOnlyList<ClientWeatherForecastDTO>? forecasts;

        protected override async Task OnInitializedAsync()
        {
            forecasts = await WeatherSvc.GetHistoryAsync();
        }
    }
}

















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025.API. All rights reserved.
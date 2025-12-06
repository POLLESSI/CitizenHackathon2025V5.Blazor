using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services;
using Microsoft.AspNetCore.Components;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastDetail : ComponentBase, IDisposable
    {
    #nullable disable
        [Inject] public HttpClient Client { get; set; }
        [Inject] public WeatherForecastService WeatherForecastService { get; set; } = default!;
        public ClientWeatherForecastDTO CurrentWeatherForecast { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                try
                {
                    CurrentWeatherForecast = await WeatherForecastService.GetByIdAsync(Id);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[WeatherForecastDetail] load {Id} failed: {ex.Message}");
                    CurrentWeatherForecast = null;
                }
            }
            else
            {
                CurrentWeatherForecast = null; // Reset if invalid Id
            }
        }
        public void Dispose()
        {
            _cts?.Cancel();
            _cts?.Dispose();
        }
    }
}






















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





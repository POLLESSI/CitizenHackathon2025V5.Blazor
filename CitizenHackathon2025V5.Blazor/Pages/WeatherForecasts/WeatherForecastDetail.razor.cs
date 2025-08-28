using CitizenHackathon2025V5.Blazor.Client.Models;
using Microsoft.AspNetCore.Components;
using Newtonsoft.Json;
using System.Threading;

namespace CitizenHackathon2025V5.Blazor.Client.Pages.WeatherForecasts
{
    public partial class WeatherForecastDetail : ComponentBase, IDisposable
    {
#nullable disable
        [Inject] public HttpClient? Client { get; set; }
        public WeatherForecastModel? CurrentWeatherForecast { get; set; }

        [Parameter] public int Id { get; set; }

        private CancellationTokenSource? _cts;
        protected override async Task OnParametersSetAsync()
        {
            // Cancels any previous request
            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            if (Id > 0)
            {
                await GetWeatherForecastAsync(_cts.Token);
            }
            else
            {
                CurrentWeatherForecast = null; // Reset if invalid Id
            }
        }
        private async Task GetWeatherForecastAsync(CancellationToken token)
        {
            try
            {
                HttpResponseMessage message = await Client.GetAsync($"api/event/{Id}", token);

                if (message.IsSuccessStatusCode)
                {
                    string json = await message.Content.ReadAsStringAsync(token);
                    CurrentWeatherForecast = JsonConvert.DeserializeObject<WeatherForecastModel>(json);
                }
                else
                {
                    CurrentWeatherForecast = null;
                }
            }
            catch (TaskCanceledException)
            {
                // Normal cancellation → we ignore
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error loading weather forecast {Id} : {ex.Message}");
                CurrentWeatherForecast = null;
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
using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class WeatherHubClient : IAsyncDisposable
    {
        private readonly NavigationManager _nav;
        private readonly IJSRuntime _js;
        private HubConnection? _connection;

        public event Action<RainAlertDTO>? HeavyRainReceived;

        public WeatherHubClient(NavigationManager nav, IJSRuntime js)
        {
            _nav = nav;
            _js = js;
        }

        public async Task StartAsync()
        {
            if (_connection != null && _connection.State != HubConnectionState.Disconnected)
                return;

            var hubUrl = _nav.ToAbsoluteUri($"/hubs/{WeatherForecastHubMethods.HubPath}");
            // WeatherForecastHubMethods.HubPath = "weatherforecastHub"

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)          // JWT is already handled via HttpClient or cookie, depending on your setup.
                .WithAutomaticReconnect()
                .Build();

            // Handler HeavyRainAlert
            _connection.On<RainAlertDTO>(
                WeatherForecastHubMethods.ToClient.HeavyRainAlert,
                async alert =>
                {
                    Console.WriteLine($"[WF-Client] Heavy rain alert: {alert.Message}");

                    // 1) Notify the Blazor UI
                    HeavyRainReceived?.Invoke(alert);

                    // 2) Leaflet: flashing marker + popup
                    try
                    {
                        await _js.InvokeVoidAsync("OutZen.notifyHeavyRain", alert);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[WF-Client] JS notifyHeavyRain failed: {ex.Message}");
                    }
                });

            await _connection.StartAsync();
            Console.WriteLine("[WF-Client] WeatherHub connection started.");
        }

        public async ValueTask DisposeAsync()
        {
            if (_connection != null)
            {
                try
                {
                    await _connection.StopAsync();
                    await _connection.DisposeAsync();
                }
                catch
                {
                    // ignore
                }
            }
        }
    }
}













































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
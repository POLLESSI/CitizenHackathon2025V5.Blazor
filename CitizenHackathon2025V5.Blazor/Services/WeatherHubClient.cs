using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class WeatherHubClient : IAsyncDisposable
    {
        private readonly IAuthService _auth;
        private readonly IJSRuntime _js;
        private readonly string _apiBaseUrl;
        private HubConnection? _connection;

        public WeatherHubClient(IAuthService auth, IJSRuntime js, IConfiguration config)
        {
            _auth = auth;
            _js = js;
            _apiBaseUrl = (config["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');
        }

        public event Action<RainAlertDTO>? HeavyRainReceived;

        public async Task StartAsync()
        {
            if (_connection != null && _connection.State != HubConnectionState.Disconnected)
                return;

            var hubUrl = $"{_apiBaseUrl}/hubs/{WeatherForecastHubMethods.HubPath}";

            _connection = new HubConnectionBuilder()
                .WithUrl(hubUrl, o =>
                {
                    o.AccessTokenProvider = async () => await _auth.GetAccessTokenAsync() ?? "";
                })
                .WithAutomaticReconnect()
                .Build();

            _connection.On<RainAlertDTO>(WeatherForecastHubMethods.ToClient.HeavyRainAlert, async alert =>
            {
                HeavyRainReceived?.Invoke(alert);
                try { await _js.InvokeVoidAsync("OutZen.notifyHeavyRain", alert); } catch { }
            });

            await _connection.StartAsync();
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
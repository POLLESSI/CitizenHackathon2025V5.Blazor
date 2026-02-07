using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class WeatherForecastHubClient : IAsyncDisposable
    {
        private readonly HubConnection _cn;
        public event Action<ClientWeatherForecastDTO>? OnForecast;
        public event Action<RainAlertDTO>? OnHeavyRain;

        public WeatherForecastHubClient(IConfiguration config, IAuthService auth)
        {
            var apiBase = (config["ApiBaseUrl"] ?? "https://localhost:7254").TrimEnd('/');

            _cn = new HubConnectionBuilder()
                .WithUrl($"{apiBase}/hubs/{WeatherForecastHubMethods.HubPath}", o =>
                {
                    o.AccessTokenProvider = async () => await auth.GetAccessTokenAsync() ?? "";
                })
                .WithAutomaticReconnect()
                .Build();

            _cn.On<ClientWeatherForecastDTO>(WeatherForecastHubMethods.ToClient.ReceiveForecast,
                dto => OnForecast?.Invoke(dto));
            _cn.On<RainAlertDTO>(WeatherForecastHubMethods.ToClient.HeavyRainAlert,
                alert => OnHeavyRain?.Invoke(alert));
        }

        public Task StartAsync() => _cn.StartAsync();
        public ValueTask DisposeAsync() => _cn.DisposeAsync();
    }
}































































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
using Blazored.Toast.Services;
using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Shared.CrowdInfo;
using CitizenHackathon2025V5.Blazor.Client.Shared.Suggestion;
using CitizenHackathon2025V5.Blazor.Client.Shared.TrafficCondition;
using CitizenHackathon2025V5.Blazor.Client.Shared.WeatherForecast;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class OutZenSignalRService : SignalRServiceBase
    {
        private readonly string _eventId;

        public event Action<CrowdInfoDTO>? OnCrowdInfoUpdated;
        public event Action<List<SuggestionDTO>>? OnSuggestionsUpdated;
        public event Action<WeatherForecastDTO>? OnWeatherUpdated;
        public event Action<TrafficConditionDTO>? OnTrafficUpdated;

        public OutZenSignalRService(
            string baseHubUrl,
            Func<Task<string?>> accessTokenProvider,
            string eventId
        ) : base(baseHubUrl, accessTokenProvider) // ✅ on passe le provider à la base
        {
            _eventId = eventId;
        }

        public async Task InitializeOutZenAsync()
        {
            // ✅ La base lira le token via AccessTokenProvider au StartAsync
            await InitializeAsync(HubName.OutZen);

            RegisterHandler<CrowdInfoDTO>("CrowdInfoUpdated", dto =>
            {
                Console.WriteLine("[OutZenSignalRService] CrowdInfo event handled");
                OnCrowdInfoUpdated?.Invoke(dto);
            });

            RegisterHandler<List<SuggestionDTO>>("SuggestionsUpdated", suggestions =>
            {
                Console.WriteLine("[OutZenSignalRService] Suggestions handled");
                OnSuggestionsUpdated?.Invoke(suggestions);
            });

            RegisterHandler<WeatherForecastDTO>("WeatherUpdated", forecast =>
            {
                Console.WriteLine("[OutZenSignalRService] Weather handled");
                OnWeatherUpdated?.Invoke(forecast);
            });

            RegisterHandler<TrafficConditionDTO>("TrafficUpdated", traffic =>
            {
                Console.WriteLine("[OutZenSignalRService] Traffic handled");
                OnTrafficUpdated?.Invoke(traffic);
            });

            await JoinEventGroupAsync(_eventId);
        }

        public async Task JoinEventGroupAsync(string eventId)
        {
            if (_hubConnection != null && IsConnected)
            {
                await _hubConnection.InvokeAsync("JoinEventGroup", eventId);
                Console.WriteLine($"[OutZenSignalRService] Joined group event-{eventId}");
            }
        }

        public async Task LeaveEventGroupAsync(string eventId)
        {
            if (_hubConnection != null && IsConnected)
            {
                await _hubConnection.InvokeAsync("LeaveEventGroup", eventId);
                Console.WriteLine($"[OutZenSignalRService] Left group event-{eventId}");
            }
        }
    }
}
























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.
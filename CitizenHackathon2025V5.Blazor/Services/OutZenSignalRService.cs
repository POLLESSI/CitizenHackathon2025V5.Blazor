using Blazored.Toast.Services;
using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.SignalR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class OutZenSignalRService : SignalRServiceBase
    {
        private readonly string _eventId;

        public event Action<ClientCrowdInfoDTO>? OnCrowdInfoUpdated;
        public event Action<List<ClientSuggestionDTO>>? OnSuggestionsUpdated;
        public event Action<ClientWeatherForecastDTO>? OnWeatherUpdated;
        public event Action<ClientTrafficConditionDTO>? OnTrafficUpdated;

        public OutZenSignalRService(
            string baseHubUrl,
            Func<Task<string?>> accessTokenProvider,
            string eventId
        ) : base(baseHubUrl, accessTokenProvider) // ? we pass the provider to the base
        {
            _eventId = eventId;
        }

        protected override string BuildFullUrl(CitizenHackathon2025V5.Blazor.Client.SignalR.HubName hub)
        {
            try
            {
                var baseUrl = base.BuildFullUrl(hub);
                if (hub == HubName.OutZen && !string.IsNullOrWhiteSpace(_eventId))
                {
                    var sep = baseUrl.Contains('?') ? "&" : "?";
                    return $"{baseUrl}{sep}eventId={Uri.EscapeDataString(_eventId)}";
                }
                return baseUrl;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in BuildFullUrl: {ex.Message}");
                throw;
            }
            
        }

        public async Task InitializeOutZenAsync()
        {
            try
            {
                // ? The database will read the token via AccessTokenProvider at StartAsync
                await InitializeAsync(HubName.OutZen);

                RegisterHandler<ClientCrowdInfoDTO>("CrowdInfoUpdated", dto =>
                {
                    Console.WriteLine("[OutZenSignalRService] CrowdInfo event handled");
                    OnCrowdInfoUpdated?.Invoke(dto);
                });

                RegisterHandler<List<ClientSuggestionDTO>>("SuggestionsUpdated", suggestions =>
                {
                    Console.WriteLine("[OutZenSignalRService] Suggestions handled");
                    OnSuggestionsUpdated?.Invoke(suggestions);
                });

                RegisterHandler<ClientWeatherForecastDTO>("WeatherUpdated", forecast =>
                {
                    Console.WriteLine("[OutZenSignalRService] Weather handled");
                    OnWeatherUpdated?.Invoke(forecast);
                });

                RegisterHandler<ClientTrafficConditionDTO>("TrafficUpdated", traffic =>
                {
                    Console.WriteLine("[OutZenSignalRService] Traffic handled");
                    OnTrafficUpdated?.Invoke(traffic);
                });

                await JoinEventGroupAsync(_eventId);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in InitializeOutZenAsync: {ex.Message}");
                throw;
            }
            
        }

        public async Task JoinEventGroupAsync(string eventId)
        {
            try
            {
                if (_hubConnection != null && IsConnected)
                {
                    await _hubConnection.InvokeAsync("JoinEventGroup", eventId);
                    Console.WriteLine($"[OutZenSignalRService] Joined group event-{eventId}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in JoinEventGroupAsync: {ex.Message}");
                throw;
            }
            
        }

        public async Task LeaveEventGroupAsync(string eventId)
        {
            try
            {
                if (_hubConnection != null && IsConnected)
                {
                    await _hubConnection.InvokeAsync("LeaveEventGroup", eventId);
                    Console.WriteLine($"[OutZenSignalRService] Left group event-{eventId}");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Unexpected error in LeaveEventGroupAsync: {ex.Message}");
                throw;
            }
        }
    }
}
























































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





using CitizenHackathon2025.Blazor.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Services.Interfaces;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public class SignalRService : ISignalRService, IAsyncDisposable
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly Dictionary<string, HubConnection> _hubConnections = new();

        // ✅ nullable events and matching interface signatures
        public event Func<object?, Task>? OnNotify;
        public event Func<CrowdInfoUIDTO, Task>? OnCrowdInfoUpdated;
        public event Func<ClientEventDTO, Task>? OnEventUpdated;
        public event Func<ClientTrafficConditionDTO, Task>? OnTrafficUpdated;
        public event Func<ClientWeatherForecastDTO, Task>? OnWeatherForecastUpdated;

        public SignalRService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task StartAsync(string hubUrl, string hubName)
        {
            if (_hubConnections.ContainsKey(hubName))
                return;

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            BindHubEvents(hubName, connection);
            await connection.StartAsync();

            _hubConnections[hubName] = connection;

            await _jsRuntime.InvokeVoidAsync("signalRClient.showToast",
                $"Connected to hub {hubName}", "SignalRService", "success");
        }

        public async Task StopAsync()
        {
            foreach (var kvp in _hubConnections)
            {
                try
                {
                    await kvp.Value.StopAsync();
                    await kvp.Value.DisposeAsync();
                }
                catch
                {
                    // Logging optionnel
                }
            }

            _hubConnections.Clear();
            await _jsRuntime.InvokeVoidAsync("signalRClient.showToast",
                "All SignalR connections have been stopped.", "SignalRService", "info");
        }

        private void BindHubEvents(string hubName, HubConnection connection)
        {
            switch (hubName)
            {
                case "CrowdHub":
                    connection.On<string>("notifynewCrowd", async msg =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast",
                            $"CrowdHub: {msg}", "CrowdHub");

                        if (OnNotify is not null)
                            await OnNotify.Invoke(msg);
                    });
                    break;

                case "SuggestionHub":
                    connection.On("NewSuggestion", async () =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast",
                            $"SuggestionHub: New suggestion available", "SuggestionHub");

                        if (OnNotify is not null)
                            await OnNotify.Invoke(null);
                    });

                    connection.On<ClientSuggestionDTO>("ReceiveSuggestion", async suggestion =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast",
                            $"SuggestionHub: Suggestion received - {suggestion.Title}", "SuggestionHub");

                        if (OnNotify is not null)
                            await OnNotify.Invoke(suggestion);
                    });
                    break;

                    // … autres hubs
            }
        }

        public async ValueTask DisposeAsync() => await StopAsync();
    }
}
















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





using CitizenHackathon2025V5.Blazor.Client.DTOs;
using CitizenHackathon2025V5.Blazor.Client.Models;
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

        // Storing SignalR connections per hub
        private readonly Dictionary<string, HubConnection> _hubConnections = new();

        // Events exposed in the interface (optional, depending on usage)
        public event Func<object, Task> OnNotify;
        public event Func<CrowdInfoUIDTO, Task> OnCrowdInfoUpdated;
        public event Func<TrafficConditionModel, Task> OnTrafficUpdated;
        public event Func<WeatherForecastModel, Task> OnWeatherForecastUpdated;

        public SignalRService(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public async Task StartAsync(string hubUrl, string hubName)
        {
            if (_hubConnections.ContainsKey(hubName))
                return; // Already logged in

            var connection = new HubConnectionBuilder()
                .WithUrl(hubUrl)
                .WithAutomaticReconnect()
                .Build();

            // Bind events by hubName
            BindHubEvents(hubName, connection);

            await connection.StartAsync();

            _hubConnections[hubName] = connection;

            await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"Connected to hub {hubName}", "SignalRService", "success");
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
                    // log si besoin
                }
            }
            _hubConnections.Clear();

            await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", "All SignalR connections have been stopped.", "SignalRService", "info");
        }

        private void BindHubEvents(string hubName, HubConnection connection)
        {
            switch (hubName)
            {
                case "AISuggestionHub":
                    // No explicit event in your server-side hub
                    break;

                case "CrowdHub":
                    connection.On<string>("notifynewCrowd", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"CrowdHub: {msg}", "CrowdHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                case "EventHub":
                    connection.On<string>("NewEvent", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"EventHub: {msg}", "EventHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                case "GPTHub":
                    connection.On<string>("notifynewGPT", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"GPTHub: {msg}", "GPTHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                case "NotificationHub":
                    // No event defined on server side
                    break;

                case "OutZenHub":
                    // No event defined on server side
                    break;

                case "PlaceHub":
                    connection.On<string>("Newplace", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"PlaceHub: {msg}", "PlaceHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                case "SuggestionHub":
                    connection.On("NewSuggestion", async () =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"SuggestionHub: New suggestion available", "SuggestionHub");
                        if (OnNotify != null) await OnNotify(null);
                    });

                    connection.On<ClientSuggestionDTO>("ReceiveSuggestion", async (suggestion) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"SuggestionHub: Suggestion received - {suggestion.Title}", "SuggestionHub");
                        if (OnNotify != null) await OnNotify(suggestion);
                    });
                    break;

                case "TrafficHub":
                    connection.On("notifynewtraffic", async () =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"TrafficHub: Traffic update", "TrafficHub");
                        if (OnNotify != null) await OnNotify(null);
                    });
                    break;

                case "UpdateHub":
                    connection.On<string>("ReceiveUpdate", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"UpdateHub: {msg}", "UpdateHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                case "UserHub":
                    connection.On<string>("UserRegistered", async (email) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"UserHub: New user {email}", "UserHub");
                        if (OnNotify != null) await OnNotify(email);
                    });
                    break;

                case "WeatherHub":
                    connection.On<string>("ReceiveForecast", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"WeatherHub: Forecast received", "WeatherHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                case "WeatherForecastHub":
                    connection.On<string>("NewWeatherForecast", async (msg) =>
                    {
                        await _jsRuntime.InvokeVoidAsync("signalRClient.showToast", $"WeatherForecastHub: New forecast", "WeatherForecastHub");
                        if (OnNotify != null) await OnNotify(msg);
                    });
                    break;

                default:
                    throw new InvalidOperationException($"Hub {hubName} not recognized for SignalR binding.");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }
    }
}















































































// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.





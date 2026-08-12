using CitizenHackathon2025.Contracts.DTOs;
using CitizenHackathon2025.Contracts.Hubs;
using Microsoft.AspNetCore.SignalR.Client;

namespace CitizenHackathon2025V5.Blazor.Client.Services
{
    public sealed class EmergencyAlertClientService : IAsyncDisposable
    {
        private HubConnection? _hub;
        public event Func<EmergencyAlertSignalRDTO, Task>? AlertUpserted;
        public event Func<Guid, string, string, Task>? AlertCancelled;
        public event Func<Guid, string, string, Task>? AlertExpired;
        public event Func<EmergencyAlertRefreshDTO, Task>? AlertsRefreshed;

        public async Task StartAsync(string hubUrl, string token)
        {
            /*
             * Avoid opening a second connection.
             */
            if (_hub is not null)
                return;

            _hub = new HubConnectionBuilder()
                .WithUrl(hubUrl,
                    options =>
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    })
                .WithAutomaticReconnect()
                .Build();

            /*
             * UPSERT
             */
            _hub.On<EmergencyAlertSignalRDTO>(EmergencyAlertHubMethods.ToClient.Upserted, async alert =>
            {
                var handler = AlertUpserted;

                if (handler is not null)
                {
                    await handler(alert);
                }
            });

            /*
             * CANCEL
             *
             * Must match exactly:
             *
             * EmergencyAlertCancelled(
             *     Guid alertId,
             *     string sourceCode,
             *     string externalId)
             */
            _hub.On<Guid, string, string>(EmergencyAlertHubMethods.ToClient.Cancelled, async (alertId, sourceCode, externalId) =>
            {
                var handler = AlertCancelled;

                if (handler is not null)
                {
                    await handler(alertId, sourceCode, externalId);
                }
            });

            /*
             * EXPIRED
             */
            _hub.On<Guid, string, string>(EmergencyAlertHubMethods.ToClient.Expired, async (alertId, sourceCode, externalId) =>
            {
                var handler = AlertExpired;

                if (handler is not null)
                {
                    await handler(alertId, sourceCode, externalId);
                }
            });

            /*
             * COMPLETE REFRESH
             */
            _hub.On<EmergencyAlertRefreshDTO>(EmergencyAlertHubMethods.ToClient.Refreshed,
                async refresh =>
                {
                    var handler = AlertsRefreshed;
                    if (handler is not null)
                    {
                        await handler(refresh);
                    }
                });

            _hub.Reconnected += async _ =>
            {
                if (_hub is null || _hub.State != HubConnectionState.Connected)
                {
                    return;
                }

                try
                {
                    await _hub.InvokeAsync(EmergencyAlertHubMethods.SubscribeAll);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[EmergencyAlertHub] " + $"Re-subscribe failed: {ex.Message}");
                }
            };


            await _hub.StartAsync();
            await _hub.InvokeAsync(EmergencyAlertHubMethods.SubscribeAll);
        }

        public async ValueTask DisposeAsync()
        {
            if (_hub is null)
                return;

            try
            {
                if (_hub.State == HubConnectionState.Connected)
                {
                    await _hub.InvokeAsync(EmergencyAlertHubMethods.UnsubscribeAll);
                }
            }
            catch
            {
                // Connection may already be gone.
            }

            await _hub.DisposeAsync();
            _hub = null;
        }
    }
}
























































































































/*// Copyrigtht (c) 2025 Citizen Hackathon https://github.com/POLLESSI/Citizenhackathon2025V5.Blazor.Client. All rights reserved.*/